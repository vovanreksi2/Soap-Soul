using SoapAndSoul.Domain.Catalog;

namespace SoapAndSoul.Client.State;

/// <summary>URL segments for the cosmetic lines: /soap, /perfume.</summary>
public static class Lines
{
    public static readonly CosmeticLine[] All = [CosmeticLine.Soap, CosmeticLine.Perfume];

    public static string Slug(CosmeticLine line) => line == CosmeticLine.Soap ? "soap" : "perfume";

    public static CosmeticLine? FromSlug(string? slug) => slug switch
    {
        "soap" => CosmeticLine.Soap,
        "perfume" => CosmeticLine.Perfume,
        _ => null,
    };

    public static string ListUrl(CosmeticLine line) => Slug(line);

    public static string RecipeUrl(CosmeticLine line, Guid id) => $"{Slug(line)}/recipes/{id}";
}
