namespace SoapAndSoul.Domain.Catalog;

/// <param name="Single">At most one ingredient of this category per recipe.</param>
/// <param name="HasCapacity">Mold / bottle: its capacity sets the recipe weight and the base amount.</param>
/// <param name="Amortized">The item is reused: its price is spread over <c>UsesPerItem</c> recipes.</param>
public sealed record CategoryDefinition(
    CategoryKey Key,
    CosmeticLine Line,
    string Label,
    string Icon,
    IReadOnlyList<MeasureUnit> Units,
    bool Required,
    string Hint,
    bool Single = false,
    bool HasCapacity = false,
    bool Amortized = false)
{
    public MeasureUnit DefaultUnit => Units[0];
}
