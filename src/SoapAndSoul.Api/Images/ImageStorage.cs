using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace SoapAndSoul.Api.Images;

public sealed class ImageStorageOptions
{
    public const string Section = "Images";

    /// <summary>Folder for uploaded photos; relative paths resolve against the content root.</summary>
    public string Path { get; set; } = "App_Data/images";

    public long MaxBytes { get; set; } = 5 * 1024 * 1024;
}

/// <summary>Where photos live. Local disk for now; an Azure Blob implementation can replace it.</summary>
public interface IImageStorage
{
    /// <returns>The public URL of the stored image.</returns>
    Task<string> SaveAsync(Stream content, string extension, CancellationToken ct);
}

public sealed class LocalImageStorage : IImageStorage
{
    public const string UrlPrefix = "/images";

    public LocalImageStorage(IOptions<ImageStorageOptions> options, IWebHostEnvironment env)
    {
        Root = System.IO.Path.GetFullPath(options.Value.Path, env.ContentRootPath);
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken ct)
    {
        var name = $"{Guid.CreateVersion7():N}{extension}";
        await using var file = File.Create(System.IO.Path.Combine(Root, name));
        await content.CopyToAsync(file, ct);
        return $"{UrlPrefix}/{name}";
    }
}

public static class ImageFilesExtensions
{
    /// <summary>Serves stored photos. Names are random and never reused, so they can be cached forever.</summary>
    public static IApplicationBuilder UseImageFiles(this WebApplication app)
    {
        var storage = app.Services.GetRequiredService<IImageStorage>();
        if (storage is not LocalImageStorage local) return app;
        return app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(local.Root),
            RequestPath = LocalImageStorage.UrlPrefix,
            OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable",
        });
    }
}
