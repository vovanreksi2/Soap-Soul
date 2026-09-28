using System.Text.RegularExpressions;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace SoapAndSoul.Api.Images;

public enum ImageStorageProvider { Local, AzureBlob }

public sealed class ImageStorageOptions
{
    public const string Section = "Images";

    public ImageStorageProvider Provider { get; set; } = ImageStorageProvider.Local;

    /// <summary>Local: folder for uploaded photos; relative paths resolve against the content root.</summary>
    public string Path { get; set; } = "App_Data/images";

    /// <summary>AzureBlob: e.g. <c>https://account.blob.core.windows.net</c>.</summary>
    public Uri? BlobServiceUri { get; set; }

    /// <summary>AzureBlob: the container, created by the infrastructure (the app has data access only).</summary>
    public string Container { get; set; } = "images";

    /// <summary>AzureBlob: client id of the user-assigned managed identity; empty uses <see cref="DefaultAzureCredential"/>.</summary>
    public string? ManagedIdentityClientId { get; set; }

    public long MaxBytes { get; set; } = 5 * 1024 * 1024;
}

/// <summary>Where photos live: local disk in development, Azure Blob Storage in production.</summary>
public interface IImageStorage
{
    Task SaveAsync(string name, Stream content, CancellationToken ct);

    /// <returns>The image content, or <c>null</c> if there is no such image.</returns>
    Task<Stream?> OpenReadAsync(string name, CancellationToken ct);
}

/// <summary>Photo names are random, never reused and served at <c>/images/{name}</c>.</summary>
internal static partial class ImageNames
{
    public const string UrlPrefix = "/images";

    public static string New(string extension) => $"{Guid.CreateVersion7():N}{extension}";

    public static string Url(string name) => $"{UrlPrefix}/{name}";

    /// <summary>Only names this app generates, which also rules out path traversal.</summary>
    public static bool IsValid(string name) => Pattern().IsMatch(name);

    public static string ContentType(string name) => System.IO.Path.GetExtension(name) switch
    {
        ".jpg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "application/octet-stream",
    };

    [GeneratedRegex(@"^[0-9a-f]{32}\.(jpg|png|webp)$")]
    private static partial Regex Pattern();
}

public sealed class LocalImageStorage(IOptions<ImageStorageOptions> options, IWebHostEnvironment env) : IImageStorage
{
    private readonly string _root = Directory.CreateDirectory(
        System.IO.Path.GetFullPath(options.Value.Path, env.ContentRootPath)).FullName;

    public async Task SaveAsync(string name, Stream content, CancellationToken ct)
    {
        await using var file = File.Create(System.IO.Path.Combine(_root, name));
        await content.CopyToAsync(file, ct);
    }

    public Task<Stream?> OpenReadAsync(string name, CancellationToken ct)
    {
        var path = System.IO.Path.Combine(_root, name);
        return Task.FromResult<Stream?>(File.Exists(path) ? File.OpenRead(path) : null);
    }
}

public sealed class BlobImageStorage(BlobContainerClient container) : IImageStorage
{
    public async Task SaveAsync(string name, Stream content, CancellationToken ct) =>
        await container.GetBlobClient(name).UploadAsync(content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = ImageNames.ContentType(name) } }, ct);

    public async Task<Stream?> OpenReadAsync(string name, CancellationToken ct)
    {
        try
        {
            var result = await container.GetBlobClient(name).DownloadStreamingAsync(cancellationToken: ct);
            return result.Value.Content;
        }
        catch (RequestFailedException e) when (e.Status == StatusCodes.Status404NotFound)
        {
            return null;
        }
    }
}

public static class ImageStorageExtensions
{
    public static IServiceCollection AddImageStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(ImageStorageOptions.Section);
        services.Configure<ImageStorageOptions>(section);
        var options = section.Get<ImageStorageOptions>() ?? new ImageStorageOptions();

        return options.Provider switch
        {
            ImageStorageProvider.Local => services.AddSingleton<IImageStorage, LocalImageStorage>(),
            ImageStorageProvider.AzureBlob => services.AddSingleton<IImageStorage>(new BlobImageStorage(CreateContainer(options))),
            _ => throw new InvalidOperationException("Images:Provider must be Local or AzureBlob."),
        };
    }

    private static BlobContainerClient CreateContainer(ImageStorageOptions options)
    {
        var serviceUri = options.BlobServiceUri
            ?? throw new InvalidOperationException("Images:BlobServiceUri is not configured.");
        TokenCredential credential = options.ManagedIdentityClientId is { Length: > 0 } clientId
            ? new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId))
            : new DefaultAzureCredential();
        return new BlobServiceClient(serviceUri, credential).GetBlobContainerClient(options.Container);
    }
}
