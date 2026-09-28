using Microsoft.AspNetCore.Components.Forms;

namespace SoapAndSoul.Client.Services;

/// <summary>Shrinks a photo in the browser before upload: phone photos are 3–10 МБ, 1280 px is plenty.</summary>
public sealed class PhotoUploader(IImageStore images)
{
    private const int MaxSide = 1280;
    private const long MaxBytes = 5 * 1024 * 1024;

    public async Task<string> UploadAsync(IBrowserFile file)
    {
        var resized = await file.RequestImageFileAsync("image/jpeg", MaxSide, MaxSide);
        await using var stream = resized.OpenReadStream(MaxBytes);
        return await images.UploadAsync(stream, "image/jpeg");
    }
}
