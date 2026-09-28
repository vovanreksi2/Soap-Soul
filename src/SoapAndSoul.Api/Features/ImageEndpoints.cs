using Microsoft.Extensions.Options;
using SoapAndSoul.Api.Images;
using SoapAndSoul.Domain.Contracts;

namespace SoapAndSoul.Api.Features;

public static class ImageEndpoints
{
    public static void MapImageEndpoints(this RouteGroupBuilder api) =>
        // No cookies are used, so antiforgery is not needed for this form post.
        api.MapPost("/images", Upload).DisableAntiforgery();

    /// <summary>Serves stored photos from any <see cref="IImageStorage"/>, so the storage account stays private.</summary>
    public static void MapImageFiles(this IEndpointRouteBuilder app) =>
        app.MapGet($"{ImageNames.UrlPrefix}/{{name}}", Download);

    private static async Task<IResult> Download(string name, IImageStorage storage, HttpContext http, CancellationToken ct)
    {
        if (!ImageNames.IsValid(name) || await storage.OpenReadAsync(name, ct) is not { } content)
            return Results.NotFound();
        // Names are random and never reused, so they can be cached forever.
        http.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        return Results.Stream(content, ImageNames.ContentType(name));
    }

    private static async Task<IResult> Upload(IFormFile file, IImageStorage storage, IOptions<ImageStorageOptions> options, CancellationToken ct)
    {
        if (file.Length == 0) return ApiResults.Invalid("file", "Файл порожній.");
        if (file.Length > options.Value.MaxBytes) return ApiResults.Invalid("file", "Фото завелике.");

        await using var stream = file.OpenReadStream();
        var header = new byte[12];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        if (DetectExtension(header.AsSpan(0, read)) is not { } extension)
            return ApiResults.Invalid("file", "Підтримуються лише JPEG, PNG і WebP.");

        stream.Position = 0;
        var name = ImageNames.New(extension);
        await storage.SaveAsync(name, stream, ct);
        return Results.Ok(new ImageUploadResult(ImageNames.Url(name)));
    }

    /// <summary>Trusts the file's magic bytes, not its declared content type.</summary>
    internal static string? DetectExtension(ReadOnlySpan<byte> h)
    {
        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return ".jpg";
        if (h.Length >= 8 && h[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return ".png";
        if (h.Length >= 12 && h[..4].SequenceEqual("RIFF"u8) && h[8..12].SequenceEqual("WEBP"u8)) return ".webp";
        return null;
    }
}
