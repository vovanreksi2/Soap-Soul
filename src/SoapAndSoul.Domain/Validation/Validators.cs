using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;

namespace SoapAndSoul.Domain.Validation;

/// <summary>Field name → messages; empty when valid. Shape matches ASP.NET validation problems.</summary>
public sealed class ValidationErrors : Dictionary<string, string[]>
{
    public bool IsValid => Count == 0;

    public void Add(string field, string message) =>
        this[field] = TryGetValue(field, out var existing) ? [.. existing, message] : [message];
}

public static class IngredientValidator
{
    public const int MaxNameLength = 200;

    public static ValidationErrors Validate(IngredientDto i)
    {
        var e = new ValidationErrors();
        if (Categories.Find(i.Line, i.Category) is not { } cat)
        {
            e.Add(nameof(i.Category), "Категорія не належить до цієї лінійки.");
            return e;
        }
        if (string.IsNullOrWhiteSpace(i.Name)) e.Add(nameof(i.Name), "Вкажіть назву.");
        else if (i.Name.Trim().Length > MaxNameLength) e.Add(nameof(i.Name), $"Назва довша за {MaxNameLength} символів.");
        if (!cat.Units.Contains(i.Unit)) e.Add(nameof(i.Unit), "Ця одиниця не підходить для категорії.");
        if (cat.HasCapacity)
        {
            if (i.Capacity is not > 0) e.Add(nameof(i.Capacity), "Вкажіть об’єм більше нуля.");
        }
        else if (i.TypicalAmount <= 0) e.Add(nameof(i.TypicalAmount), "Типова порція має бути більше нуля.");
        if (i.PurchaseQuantity <= 0) e.Add(nameof(i.PurchaseQuantity), "Кількість покупки має бути більше нуля.");
        if (i.PurchasePrice <= 0) e.Add(nameof(i.PurchasePrice), "Ціна має бути більше нуля.");
        if (i.UsesPerItem < 1) e.Add(nameof(i.UsesPerItem), "Кількість використань — щонайменше 1.");
        return e;
    }
}

public static class RecipeValidator
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 4000;

    /// <param name="lookup">Resolves ingredients referenced by the recipe (null = unknown or deleted).</param>
    public static ValidationErrors Validate(RecipeDto r, Func<Guid, IngredientDto?> lookup)
    {
        var e = new ValidationErrors();
        if (string.IsNullOrWhiteSpace(r.Name)) e.Add(nameof(r.Name), "Вкажіть назву.");
        else if (r.Name.Length > MaxNameLength) e.Add(nameof(r.Name), $"Назва довша за {MaxNameLength} символів.");
        if (r.Description.Length > MaxDescriptionLength) e.Add(nameof(r.Description), "Опис задовгий.");
        if (r.Weight < 0) e.Add(nameof(r.Weight), "Вага не може бути від’ємною.");
        if (r.TimeMinutes < 0) e.Add(nameof(r.TimeMinutes), "Час не може бути від’ємним.");
        if (r.BatchSize < 1) e.Add(nameof(r.BatchSize), "Партія — щонайменше 1 шт.");

        if (r.Items.GroupBy(i => i.IngredientId).Any(g => g.Count() > 1))
            e.Add(nameof(r.Items), "Інгредієнт додано двічі.");

        var categories = new List<CategoryKey>();
        foreach (var item in r.Items)
        {
            var ing = lookup(item.IngredientId);
            if (ing is null) e.Add(nameof(r.Items), $"Інгредієнт {item.IngredientId} не знайдено.");
            else if (ing.Line != r.Line) e.Add(nameof(r.Items), $"«{ing.Name}» з іншої лінійки.");
            else categories.Add(ing.Category);
            if (item.Amount < 0) e.Add(nameof(r.Items), "Кількість не може бути від’ємною.");
        }

        foreach (var cat in Categories.For(r.Line))
        {
            if (cat.Single && categories.Count(c => c == cat.Key) > 1)
                e.Add(nameof(r.Items), $"«{cat.Label}» може бути лише одна.");
            if (Categories.ExclusiveWith(r.Line, cat.Key) is { } other && cat.Key < other
                && categories.Contains(cat.Key) && categories.Contains(other))
                e.Add(nameof(r.Items), "Запашки й ефірні масла не поєднуються.");
        }
        return e;
    }
}
