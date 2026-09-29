namespace SoapAndSoul.Client.Services;

/// <summary>
/// Photo sizes. Supplier-catalog photos are 200×200 thumbnails; the supplier serves the same
/// picture larger by changing the size in the file name. Uploaded photos are already large.
/// </summary>
public static class PhotoUrls
{
    private const string SupplierPhotos = "aromasoap.com.ua/files/products/";
    private const string SupplierThumb = ".200x200.";
    private const string SupplierLarge = ".800x600w.";

    public static string Large(string url) =>
        url.Contains(SupplierPhotos, StringComparison.Ordinal)
            ? url.Replace(SupplierThumb, SupplierLarge, StringComparison.Ordinal)
            : url;
}
