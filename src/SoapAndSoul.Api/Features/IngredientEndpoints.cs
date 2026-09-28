using Microsoft.EntityFrameworkCore;
using SoapAndSoul.Data;
using SoapAndSoul.Data.Entities;
using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;
using SoapAndSoul.Domain.Text;
using SoapAndSoul.Domain.Validation;

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

    private static async Task<IResult> List(CosmeticLine line, SoapAndSoulDbContext db, CancellationToken ct)
    {
        var items = await db.Ingredients.AsNoTracking().Where(i => i.Line == line).ToListAsync(ct);
        return Results.Ok(items
            .OrderBy(i => i.Category)
            .ThenBy(i => i.Name, UkrainianComparer.Instance)
            .Select(i => i.ToDto()));
    }

    private static async Task<IResult> Upsert(Guid id, IngredientDto dto, SoapAndSoulDbContext db, CancellationToken ct)
    {
        if (dto.Id != id) return ApiResults.Invalid(nameof(dto.Id), "Id у шляху й у тілі не збігаються.");
        var errors = IngredientValidator.Validate(dto);
        if (!errors.IsValid) return ApiResults.Invalid(errors);

        var now = DateTimeOffset.UtcNow;
        var entity = await db.Ingredients.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == id, ct);
        var created = entity is null;
        if (entity is null)
        {
            entity = new Ingredient { Id = id, CreatedAt = now };
            db.Ingredients.Add(entity);
        }
        else
        {
            if (entity.IsDeleted) return Results.NotFound();
            if (entity.Version != dto.Version) return ApiResults.Conflict(entity.ToDto());
            if (entity.Line != dto.Line || entity.Category != dto.Category)
                return ApiResults.Invalid(nameof(dto.Category), "Лінійку й категорію інгредієнта змінити не можна.");
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
            var current = await db.Ingredients.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
            return current is null ? Results.NotFound() : ApiResults.Conflict(current.ToDto());
        }
        return created ? Results.Created($"/api/ingredients/{id}", entity.ToDto()) : Results.Ok(entity.ToDto());
    }

    /// <summary>Soft-deletes the ingredient and removes it from every recipe that used it.</summary>
    private static async Task<IResult> Delete(Guid id, SoapAndSoulDbContext db, CancellationToken ct)
    {
        var entity = await db.Ingredients.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (entity is null) return Results.NoContent();

        var now = DateTimeOffset.UtcNow;
        var recipes = await db.Recipes.Include(r => r.Items).Where(r => r.Items.Any(i => i.IngredientId == id)).ToListAsync(ct);
        foreach (var recipe in recipes)
        {
            recipe.Items.RemoveAll(i => i.IngredientId == id);
            recipe.UpdatedAt = now;
            recipe.Version = Guid.NewGuid();
        }
        entity.IsDeleted = true;
        entity.UpdatedAt = now;
        entity.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
