using Microsoft.EntityFrameworkCore;
using SoapAndSoul.Data;
using SoapAndSoul.Data.Entities;
using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;
using SoapAndSoul.Domain.Validation;

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

    private static async Task<IResult> List(CosmeticLine line, SoapAndSoulDbContext db, CancellationToken ct)
    {
        var recipes = await db.Recipes.AsNoTracking().Include(r => r.Items).Where(r => r.Line == line).ToListAsync(ct);
        return Results.Ok(recipes.Select(r => r.ToDto()));
    }

    private static async Task<IResult> Get(Guid id, SoapAndSoulDbContext db, CancellationToken ct) =>
        await db.Recipes.AsNoTracking().Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id, ct) is { } r
            ? Results.Ok(r.ToDto())
            : Results.NotFound();

    /// <summary>Saves the whole recipe document: metadata and the full list of items.</summary>
    private static async Task<IResult> Upsert(Guid id, RecipeDto dto, SoapAndSoulDbContext db, CancellationToken ct)
    {
        if (dto.Id != id) return ApiResults.Invalid(nameof(dto.Id), "Id у шляху й у тілі не збігаються.");

        var ingredientIds = dto.Items.Select(i => i.IngredientId).Distinct().ToList();
        var ingredients = await db.Ingredients.AsNoTracking()
            .Where(i => ingredientIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.ToDto(), ct);
        var errors = RecipeValidator.Validate(dto, ingredients.GetValueOrDefault);
        if (!errors.IsValid) return ApiResults.Invalid(errors);

        var now = DateTimeOffset.UtcNow;
        var entity = await db.Recipes.IgnoreQueryFilters().Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id, ct);
        var created = entity is null;
        if (entity is null)
        {
            entity = new Recipe { Id = id, Line = dto.Line, CreatedAt = now };
            db.Recipes.Add(entity);
        }
        else
        {
            if (entity.IsDeleted) return Results.NotFound();
            if (entity.Version != dto.Version) return ApiResults.Conflict(entity.ToDto());
            if (entity.Line != dto.Line) return ApiResults.Invalid(nameof(dto.Line), "Лінійку рецепта змінити не можна.");
        }

        entity.Apply(dto);
        entity.UpdatedAt = now;
        entity.Version = Guid.NewGuid();
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var current = await db.Recipes.AsNoTracking().Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id, ct);
            return current is null ? Results.NotFound() : ApiResults.Conflict(current.ToDto());
        }
        return created ? Results.Created($"/api/recipes/{id}", entity.ToDto()) : Results.Ok(entity.ToDto());
    }

    private static async Task<IResult> Delete(Guid id, SoapAndSoulDbContext db, CancellationToken ct)
    {
        var entity = await db.Recipes.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (entity is null) return Results.NoContent();
        entity.IsDeleted = true;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
