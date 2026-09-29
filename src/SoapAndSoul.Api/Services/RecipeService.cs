using Microsoft.EntityFrameworkCore;
using SoapAndSoul.Data;
using SoapAndSoul.Data.Entities;
using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;
using SoapAndSoul.Domain.Validation;

namespace SoapAndSoul.Api.Services;

/// <summary>Recipe operations shared by the REST API and the MCP server.</summary>
public sealed class RecipeService(SoapAndSoulDbContext db)
{
    public async Task<IReadOnlyList<RecipeDto>> ListAsync(CosmeticLine line, CancellationToken ct)
    {
        var recipes = await db.Recipes.AsNoTracking().Include(r => r.Items).Where(r => r.Line == line).ToListAsync(ct);
        return recipes.Select(r => r.ToDto()).ToList();
    }

    public async Task<RecipeDto?> GetAsync(Guid id, CancellationToken ct) =>
        (await db.Recipes.AsNoTracking().Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id, ct))?.ToDto();

    /// <summary>Saves the whole recipe document: metadata and the full list of items.</summary>
    public async Task<SaveOutcome<RecipeDto>> SaveAsync(RecipeDto dto, CancellationToken ct)
    {
        var ingredientIds = dto.Items.Select(i => i.IngredientId).Distinct().ToList();
        var ingredients = await db.Ingredients.AsNoTracking()
            .Where(i => ingredientIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.ToDto(), ct);
        var errors = RecipeValidator.Validate(dto, ingredients.GetValueOrDefault);
        if (!errors.IsValid) return new SaveOutcome<RecipeDto>.Invalid(errors);

        var now = DateTimeOffset.UtcNow;
        var entity = await db.Recipes.IgnoreQueryFilters().Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == dto.Id, ct);
        var created = entity is null;
        if (entity is null)
        {
            entity = new Recipe { Id = dto.Id, Line = dto.Line, CreatedAt = now };
            db.Recipes.Add(entity);
        }
        else
        {
            if (entity.IsDeleted) return new SaveOutcome<RecipeDto>.NotFound();
            if (entity.Version != dto.Version) return new SaveOutcome<RecipeDto>.Conflict(entity.ToDto());
            if (entity.Line != dto.Line) return SaveOutcome<RecipeDto>.Invalid.Of(nameof(dto.Line), "Лінійку рецепта змінити не можна.");
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
            return await GetAsync(dto.Id, ct) is { } current
                ? new SaveOutcome<RecipeDto>.Conflict(current)
                : new SaveOutcome<RecipeDto>.NotFound();
        }
        return new SaveOutcome<RecipeDto>.Saved(entity.ToDto(), created);
    }

    /// <returns>False when there was nothing to delete.</returns>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var entity = await db.Recipes.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (entity is null) return false;
        entity.IsDeleted = true;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return true;
    }
}
