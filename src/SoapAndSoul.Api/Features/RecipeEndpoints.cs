using SoapAndSoul.Api.Services;
using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;

namespace SoapAndSoul.Api.Features;

public static class RecipeEndpoints
{
    public static void MapRecipeEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/recipes");
        g.MapGet("", List);
        g.MapGet("/{id:guid}", Get);
        g.MapPut("/{id:guid}", Upsert);
        g.MapDelete("/{id:guid}", Delete);
    }

    private static async Task<IResult> List(CosmeticLine line, RecipeService recipes, CancellationToken ct) =>
        Results.Ok(await recipes.ListAsync(line, ct));

    private static async Task<IResult> Get(Guid id, RecipeService recipes, CancellationToken ct) =>
        await recipes.GetAsync(id, ct) is { } r ? Results.Ok(r) : Results.NotFound();

    /// <summary>Saves the whole recipe document: metadata and the full list of items.</summary>
    private static async Task<IResult> Upsert(Guid id, RecipeDto dto, RecipeService recipes, CancellationToken ct)
    {
        if (dto.Id != id) return ApiResults.Invalid(nameof(dto.Id), "Id у шляху й у тілі не збігаються.");
        return (await recipes.SaveAsync(dto, ct)).ToResult(r => $"/api/recipes/{r.Id}");
    }

    private static async Task<IResult> Delete(Guid id, RecipeService recipes, CancellationToken ct)
    {
        await recipes.DeleteAsync(id, ct);
        return Results.NoContent();
    }
}
