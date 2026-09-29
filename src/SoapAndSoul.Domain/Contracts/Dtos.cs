using SoapAndSoul.Domain.Catalog;

namespace SoapAndSoul.Domain.Contracts;

/// <summary>An ingredient (component) with its purchase price.</summary>
/// <param name="TypicalAmount">Default amount added to a recipe; 1 for mold/bottle.</param>
/// <param name="Capacity">Mold weight (г) or bottle volume (мл); only for categories with capacity.</param>
/// <param name="UsesPerItem">How many recipes one purchased item serves (molds are reused); 1 otherwise.</param>
/// <param name="Version">Optimistic-concurrency token; null when the ingredient is new.</param>
public sealed record IngredientDto(
    Guid Id,
    CosmeticLine Line,
    CategoryKey Category,
    string Name,
    MeasureUnit Unit,
    decimal TypicalAmount,
    decimal PurchaseQuantity,
    decimal PurchasePrice,
    decimal? Capacity,
    int UsesPerItem,
    string? PhotoUrl,
    Guid? Version = null);

public sealed record RecipeItemDto(Guid IngredientId, decimal Amount);

/// <summary>A recipe saved as one document: metadata and its full composition.</summary>
/// <param name="Weight">Weight of one piece (г for soap, мл for perfume).</param>
/// <param name="Version">Optimistic-concurrency token; null when the recipe is new.</param>
public sealed record RecipeDto(
    Guid Id,
    CosmeticLine Line,
    string Name,
    string Description,
    decimal Weight,
    int TimeMinutes,
    int BatchSize,
    string? PhotoUrl,
    IReadOnlyList<RecipeItemDto> Items,
    Guid? Version = null,
    DateTimeOffset? UpdatedAt = null);

public sealed record ImageUploadResult(string Url);

/// <summary>Dictated text (from speech recognition) to turn into a recipe draft.</summary>
public sealed record RecipeDraftRequest(CosmeticLine Line, string Transcript)
{
    public const int MaxTranscriptLength = 4000;
}

/// <summary>An ingredient mentioned in a dictated recipe.</summary>
/// <param name="SpokenName">How the user called it; shown when it matched nothing in the catalog.</param>
/// <param name="IngredientId">Matched catalog ingredient; null when nothing matched.</param>
/// <param name="Amount">Amount as dictated, in <paramref name="Unit"/>; null when not said.</param>
public sealed record RecipeDraftItemDto(
    string SpokenName,
    Guid? IngredientId,
    CategoryKey? Category,
    decimal? Amount,
    MeasureUnit? Unit);

/// <summary>What the user dictated, structured; null fields were not mentioned.</summary>
/// <param name="Notes">Remarks about parts of the dictation that could not be interpreted.</param>
public sealed record RecipeDraftDto(
    string? Name,
    string? Description,
    decimal? Weight,
    int? TimeMinutes,
    int? BatchSize,
    IReadOnlyList<RecipeDraftItemDto> Items,
    IReadOnlyList<string> Notes);

/// <summary>Optional server features the client adapts to.</summary>
public sealed record FeaturesDto(bool VoiceDrafts);
