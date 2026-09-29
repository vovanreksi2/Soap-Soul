using SoapAndSoul.Api.Services;
using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;

namespace SoapAndSoul.Api.Features;

public static class IngredientEndpoints
{
    public static void MapIngredientEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/ingredients");
        g.MapGet("", List);
        g.MapPut("/{id:guid}", Upsert);
        g.MapDelete("/{id:guid}", Delete);
    }

    private static async Task<IResult> List(CosmeticLine line, IngredientService ingredients, CancellationToken ct) =>
        Results.Ok(await ingredients.ListAsync(line, ct));

    private static async Task<IResult> Upsert(Guid id, IngredientDto dto, IngredientService ingredients, CancellationToken ct)
    {
        if (dto.Id != id) return ApiResults.Invalid(nameof(dto.Id), "Id у шляху й у тілі не збігаються.");
        return (await ingredients.SaveAsync(dto, ct)).ToResult(i => $"/api/ingredients/{i.Id}");
    }

    /// <summary>Soft-deletes the ingredient and removes it from every recipe that used it.</summary>
    private static async Task<IResult> Delete(Guid id, IngredientService ingredients, CancellationToken ct)
    {
        await ingredients.DeleteAsync(id, ct);
        return Results.NoContent();
    }
}
