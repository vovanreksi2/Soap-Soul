using static SoapAndSoul.Domain.Catalog.MeasureUnit;

namespace SoapAndSoul.Domain.Catalog;

/// <summary>The fixed set of ingredient categories per cosmetic line, in display order.</summary>
public static class Categories
{
    public const int DefaultMoldUses = 100;

    private static readonly IReadOnlyList<CategoryDefinition> Soap =
    [
        new(CategoryKey.Mold, CosmeticLine.Soap, "Форма", "ph-shapes", [Piece], Required: true,
            "Одна форма. Основа й вага рецепта підлаштуються під її об’єм.", Single: true, HasCapacity: true, Amortized: true),
        new(CategoryKey.SoapBase, CosmeticLine.Soap, "Основа", "ph-cube", [Gram], Required: true,
            "Кількість = об’єм обраної форми.", Single: true),
        new(CategoryKey.Pigment, CosmeticLine.Soap, "Колір", "ph-palette", [Drop, Gram], Required: true,
            "Можна кілька відтінків."),
        new(CategoryKey.Extract, CosmeticLine.Soap, "Екстракт", "ph-test-tube", [Milliliter, Drop], Required: true,
            "Можна кілька."),
        new(CategoryKey.Fragrance, CosmeticLine.Soap, "Запашка", "ph-drop", [Drop], Required: false,
            "Можна кілька. Не поєднуються з ефірними маслами."),
        new(CategoryKey.EssentialOil, CosmeticLine.Soap, "Ефірне масло", "ph-leaf", [Drop], Required: false,
            "Не поєднуються з запашками — вибір прибере їх."),
        new(CategoryKey.Packaging, CosmeticLine.Soap, "Пакування", "ph-package", [Piece], Required: true,
            "Коробка, етикетка, стрічка…"),
        new(CategoryKey.Tools, CosmeticLine.Soap, "Інструменти", "ph-wrench", [Piece], Required: false,
            "Витратні інструменти на одну штуку."),
    ];

    private static readonly IReadOnlyList<CategoryDefinition> Perfume =
    [
        new(CategoryKey.Bottle, CosmeticLine.Perfume, "Флакон", "ph-flask", [Piece], Required: true,
            "Один флакон. Основа підлаштується під його об’єм.", Single: true, HasCapacity: true),
        new(CategoryKey.PerfumeBase, CosmeticLine.Perfume, "Основа", "ph-drop-half", [Milliliter], Required: true,
            "Кількість = об’єм обраного флакона.", Single: true),
        new(CategoryKey.Fragrance, CosmeticLine.Perfume, "Запашка", "ph-drop", [Drop], Required: true,
            "Можна кілька."),
    ];

    public static IReadOnlyList<CategoryDefinition> For(CosmeticLine line) => line switch
    {
        CosmeticLine.Soap => Soap,
        CosmeticLine.Perfume => Perfume,
        _ => throw new ArgumentOutOfRangeException(nameof(line), line, null),
    };

    public static CategoryDefinition? Find(CosmeticLine line, CategoryKey key) =>
        For(line).FirstOrDefault(c => c.Key == key);

    public static CategoryDefinition Get(CosmeticLine line, CategoryKey key) =>
        Find(line, key) ?? throw new ArgumentException($"Category {key} does not exist in line {line}.", nameof(key));

    public static CategoryKey BaseOf(CosmeticLine line) =>
        line == CosmeticLine.Soap ? CategoryKey.SoapBase : CategoryKey.PerfumeBase;

    /// <summary>Unit of the recipe weight and of mold/bottle capacity.</summary>
    public static MeasureUnit CapacityUnit(CosmeticLine line) =>
        line == CosmeticLine.Soap ? Gram : Milliliter;

    /// <summary>Categories that cannot be combined with <paramref name="key"/> in one recipe.</summary>
    public static CategoryKey? ExclusiveWith(CosmeticLine line, CategoryKey key) => (line, key) switch
    {
        (CosmeticLine.Soap, CategoryKey.Fragrance) => CategoryKey.EssentialOil,
        (CosmeticLine.Soap, CategoryKey.EssentialOil) => CategoryKey.Fragrance,
        _ => null,
    };

    public static string LineIcon(CosmeticLine line) => line == CosmeticLine.Soap ? "ph-hand-soap" : "ph-sparkle";

    public static string LineLabel(CosmeticLine line) => line == CosmeticLine.Soap ? "Мило" : "Парфуми";
}
