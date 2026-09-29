using Microsoft.EntityFrameworkCore;
using SoapAndSoul.Data;
using SoapAndSoul.Data.Entities;
using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;
using SoapAndSoul.Domain.Text;
using SoapAndSoul.Domain.Validation;

namespace SoapAndSoul.Api.Services;

/// <summary>Ingredient operations shared by the REST API and the MCP server.</summary>
public sealed class IngredientService(SoapAndSoulDbContext db)
{
    public async Task<IReadOnlyList<IngredientDto>> ListAsync(CosmeticLine line, CancellationToken ct)
    {
        var items = await db.Ingredients.AsNoTracking().Where(i => i.Line == line).ToListAsync(ct);
        return items
            .OrderBy(i => i.Category)
            .ThenBy(i => i.Name, UkrainianComparer.Instance)
            .Select(i => i.ToDto())
            .ToList();
    }

    public async Task<IngredientDto?> GetAsync(Guid id, CancellationToken ct) =>
        (await db.Ingredients.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct))?.ToDto();

    public async Task<SaveOutcome<IngredientDto>> SaveAsync(IngredientDto dto, CancellationToken ct)
    {
        var errors = IngredientValidator.Validate(dto);
        if (!errors.IsValid) return new SaveOutcome<IngredientDto>.Invalid(errors);

        var now = DateTimeOffset.UtcNow;
        var entity = await db.Ingredients.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == dto.Id, ct);
        var created = entity is null;
        if (entity is null)
        {
            entity = new Ingredient { Id = dto.Id, CreatedAt = now };
            db.Ingredients.Add(entity);
        }
        else
        {
            if (entity.IsDeleted) return new SaveOutcome<IngredientDto>.NotFound();
            if (entity.Version != dto.Version) return new SaveOutcome<IngredientDto>.Conflict(entity.ToDto());
            if (entity.Line != dto.Line || entity.Category != dto.Category)
                return SaveOutcome<IngredientDto>.Invalid.Of(nameof(dto.Category), "Лінійку й категорію інгредієнта змінити не можна.");
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
                ? new SaveOutcome<IngredientDto>.Conflict(current)
                : new SaveOutcome<IngredientDto>.NotFound();
        }
        return new SaveOutcome<IngredientDto>.Saved(entity.ToDto(), created);
    }

    /// <summary>Soft-deletes the ingredient and removes it from every recipe that used it.</summary>
    /// <returns>False when there was nothing to delete.</returns>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var entity = await db.Ingredients.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (entity is null) return false;

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
        return true;
    }
}
