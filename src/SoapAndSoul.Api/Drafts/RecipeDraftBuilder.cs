using System.Text;
using System.Text.Json.Nodes;
using SoapAndSoul.Api.Llm;
using SoapAndSoul.Api.Services;
using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;
using SoapAndSoul.Domain.Text;
using SoapAndSoul.Domain.Validation;

namespace SoapAndSoul.Api.Drafts;

/// <summary>
/// Turns a dictated recipe (speech already recognized on the device) into a structured draft with the
/// help of a language model. The model only reads; the client applies the draft with the domain rules.
/// </summary>
public sealed class RecipeDraftBuilder(ILlmClient llm, IngredientService ingredients)
{
    public async Task<RecipeDraftDto> BuildAsync(RecipeDraftRequest request, CancellationToken ct)
    {
        var catalog = await ingredients.ListAsync(request.Line, ct);
        var raw = await llm.CompleteJsonAsync<RawDraft>(
            new LlmJsonRequest(SystemPrompt, Prompt(request, catalog), Schema(request.Line)), ct);
        return Sanitize(raw, request.Line, catalog);
    }

    private const string SystemPrompt =
        """
        You turn a dictated description of a handmade cosmetics recipe into structured data for the
        Soap & Soul app. The user speaks Ukrainian. The transcript comes from on-device speech
        recognition, so expect misheard words, missing punctuation, filler words and numbers written
        as words ("п'ятнадцять крапель").

        Ingredients:
        - Match every ingredient the user mentions to the catalog in the request, by meaning. Allow for
          misrecognized words, Ukrainian inflection and informal names ("лаванда" for "Ефірна олія лаванди").
        - Put the catalog id in ingredientId only when the match is clear; otherwise use null and give your
          best guess of the category. Never make up an id.
        - spokenName is the ingredient as the user said it, in the nominative case.
        - amount and unit are exactly what the user said (unit null when no amount was given). Do not convert units.

        Recipe fields: fill only what the user actually said and leave the rest null. name and description
        stay in Ukrainian, cleaned of filler words; description holds preparation steps or notes, not the
        ingredient list. weight is the weight of one piece, timeMinutes the preparation time, batchSize
        the number of pieces made at once.

        notes: short remarks in Ukrainian about anything you could not interpret; an empty list otherwise.
        """;

    private static string Prompt(RecipeDraftRequest request, IReadOnlyList<IngredientDto> catalog)
    {
        var line = request.Line;
        var sb = new StringBuilder();
        sb.AppendLine($"Product line: {line} ({Categories.LineLabel(line)}). Weight is in {Categories.CapacityUnit(line)}.");
        sb.AppendLine();
        sb.AppendLine("Categories (key: label — hint):");
        foreach (var c in Categories.For(line))
            sb.AppendLine($"- {c.Key}: {c.Label} — {c.Hint}");
        sb.AppendLine();
        sb.AppendLine("Catalog (id | category | name | unit):");
        if (catalog.Count == 0) sb.AppendLine("(empty)");
        foreach (var i in catalog)
            sb.AppendLine($"- {i.Id} | {i.Category} | {i.Name} | {i.Unit}");
        sb.AppendLine();
        sb.AppendLine("<transcript>");
        sb.AppendLine(request.Transcript.Trim());
        sb.AppendLine("</transcript>");
        return sb.ToString();
    }

    private static JsonObject Schema(CosmeticLine line)
    {
        static JsonObject Nullable(JsonObject type) => new() { ["anyOf"] = new JsonArray(type, new JsonObject { ["type"] = "null" }) };
        static JsonObject Type(string type) => new() { ["type"] = type };
        static JsonObject Enum(IEnumerable<string> values) =>
            new() { ["type"] = "string", ["enum"] = new JsonArray([.. values.Select(v => JsonValue.Create(v))]) };
        static JsonObject Object(params (string Name, JsonObject Schema)[] properties) => new()
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(properties.Select(p => KeyValuePair.Create(p.Name, (JsonNode?)p.Schema))),
            ["required"] = new JsonArray([.. properties.Select(p => JsonValue.Create(p.Name))]),
            ["additionalProperties"] = false,
        };

        var item = Object(
            ("spokenName", Type("string")),
            ("ingredientId", Nullable(Type("string"))),
            ("category", Nullable(Enum(Categories.For(line).Select(c => c.Key.ToString())))),
            ("amount", Nullable(Type("number"))),
            ("unit", Nullable(Enum(System.Enum.GetNames<MeasureUnit>()))));
        return Object(
            ("name", Nullable(Type("string"))),
            ("description", Nullable(Type("string"))),
            ("weight", Nullable(Type("number"))),
            ("timeMinutes", Nullable(Type("integer"))),
            ("batchSize", Nullable(Type("integer"))),
            ("items", new JsonObject { ["type"] = "array", ["items"] = item }),
            ("notes", new JsonObject { ["type"] = "array", ["items"] = Type("string") }));
    }

    /// <summary>Keeps only what the app can use: ids from this line's catalog, valid enums and numbers.</summary>
    internal static RecipeDraftDto Sanitize(RawDraft raw, CosmeticLine line, IReadOnlyList<IngredientDto> catalog)
    {
        var byId = catalog.ToDictionary(i => i.Id);
        var items = new List<RecipeDraftItemDto>();
        foreach (var r in raw.Items ?? [])
        {
            var spoken = r.SpokenName?.Trim() ?? "";
            CategoryKey? category = Enum.TryParse<CategoryKey>(r.Category, out var c) && Categories.Find(line, c) is not null ? c : null;
            var ingredient = Guid.TryParse(r.IngredientId, out var id) ? byId.GetValueOrDefault(id) : null;
            ingredient ??= SingleFuzzyMatch(spoken, category, catalog);
            if (ingredient is null && spoken.Length == 0) continue;

            items.Add(new RecipeDraftItemDto(
                spoken.Length > 0 ? spoken : ingredient!.Name,
                ingredient?.Id,
                ingredient?.Category ?? category,
                r.Amount is > 0 ? r.Amount : null,
                Enum.TryParse<MeasureUnit>(r.Unit, out var unit) ? unit : null));
        }

        return new RecipeDraftDto(
            Clip(raw.Name, RecipeValidator.MaxNameLength),
            Clip(raw.Description, RecipeValidator.MaxDescriptionLength),
            raw.Weight is > 0 ? raw.Weight : null,
            raw.TimeMinutes is >= 0 ? raw.TimeMinutes : null,
            raw.BatchSize is >= 1 ? raw.BatchSize : null,
            items,
            (raw.Notes ?? []).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList());
    }

    /// <summary>A second chance for names the model left unmatched: accept only an unambiguous hit.</summary>
    private static IngredientDto? SingleFuzzyMatch(string spoken, CategoryKey? category, IReadOnlyList<IngredientDto> catalog)
    {
        if (spoken.Length < 3) return null;
        var hits = catalog.Where(i => (category is null || i.Category == category) && FuzzyMatcher.Matches(spoken, i.Name)).Take(2).ToList();
        return hits.Count == 1 ? hits[0] : null;
    }

    private static string? Clip(string? text, int max)
    {
        var t = text?.Trim();
        return string.IsNullOrEmpty(t) ? null : t.Length <= max ? t : t[..max];
    }

    internal sealed record RawDraft(
        string? Name, string? Description, decimal? Weight, int? TimeMinutes, int? BatchSize,
        List<RawItem>? Items, List<string>? Notes);

    internal sealed record RawItem(string? SpokenName, string? IngredientId, string? Category, decimal? Amount, string? Unit);
}
