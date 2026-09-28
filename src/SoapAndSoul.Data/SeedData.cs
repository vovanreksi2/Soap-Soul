using Microsoft.EntityFrameworkCore;
using SoapAndSoul.Data.Entities;
using SoapAndSoul.Domain.Catalog;
using static SoapAndSoul.Domain.Catalog.CategoryKey;
using static SoapAndSoul.Domain.Catalog.MeasureUnit;

namespace SoapAndSoul.Data;

/// <summary>Sample ingredients and recipes for a fresh development database.</summary>
public static class SeedData
{
    public static async Task SeedIfEmptyAsync(SoapAndSoulDbContext db, CancellationToken ct = default)
    {
        if (await db.Ingredients.IgnoreQueryFilters().AnyAsync(ct)) return;

        var now = DateTimeOffset.UtcNow;
        var ingredients = new Dictionary<string, Ingredient>();

        void I(string key, CosmeticLine line, CategoryKey cat, string name, MeasureUnit unit, decimal typical,
            decimal qty, decimal price, decimal? capacity = null, int uses = 1) =>
            ingredients[key] = new Ingredient
            {
                Id = Guid.CreateVersion7(), Line = line, Category = cat, Name = name, Unit = unit, TypicalAmount = typical,
                PurchaseQuantity = qty, PurchasePrice = price, Capacity = capacity, UsesPerItem = uses,
                CreatedAt = now, UpdatedAt = now, Version = Guid.NewGuid(),
            };

        const CosmeticLine S = CosmeticLine.Soap, P = CosmeticLine.Perfume;
        I("m1", S, Mold, "Лаванда еліт-форма", Piece, 1, 1, 180, 100, Categories.DefaultMoldUses);
        I("m2", S, Mold, "Прямокутна класична", Piece, 1, 1, 120, 120, Categories.DefaultMoldUses);
        I("m3", S, Mold, "Кругла мандала", Piece, 1, 1, 150, 90, Categories.DefaultMoldUses);
        I("b1", S, SoapBase, "Crystal Donkey Milk біла", Gram, 100, 1000, 420);
        I("b2", S, SoapBase, "Прозора гліцеринова", Gram, 100, 1000, 360);
        I("b3", S, SoapBase, "Козяче молоко", Gram, 100, 1000, 480);
        I("f1", S, Fragrance, "Лаванда і ваніль", Drop, 15, 10, 150);
        I("f2", S, Fragrance, "Біла кава", Drop, 15, 10, 130);
        I("p1", S, Pigment, "Перламутровий фіолетовий", Drop, 3, 10, 90);
        I("p2", S, Pigment, "Мідна слюда", Gram, 1, 10, 60);
        I("e1", S, EssentialOil, "Лаванда французька", Drop, 12, 10, 210);
        I("e2", S, EssentialOil, "Апельсин солодкий", Drop, 14, 10, 120);
        I("e3", S, EssentialOil, "Евкаліпт", Drop, 10, 10, 140);
        I("x1", S, Extract, "Ромашка CO₂", Milliliter, 3, 30, 150);
        I("x2", S, Extract, "Алое вера", Milliliter, 5, 30, 110);
        I("t1", S, Tools, "Рукавички", Piece, 1, 100, 250);
        I("o1", S, Packaging, "Крафт-коробка", Piece, 1, 50, 400);
        I("o2", S, Packaging, "Етикетка", Piece, 1, 100, 250);
        I("v1", P, Bottle, "Флакон-спрей 30 мл", Piece, 1, 1, 45, 30);
        I("v2", P, Bottle, "Роллер 10 мл", Piece, 1, 1, 22, 10);
        I("pb1", P, PerfumeBase, "Парфумерний спирт", Milliliter, 30, 500, 380);
        I("pb2", P, PerfumeBase, "Олія жожоба", Milliliter, 10, 100, 260);
        I("pf1", P, Fragrance, "Жасмин самбак", Drop, 40, 10, 190);
        I("pf2", P, Fragrance, "Бергамот", Drop, 12, 10, 140);
        I("pf3", P, Fragrance, "Кедр атласький", Drop, 8, 10, 160);
        I("pf4", P, Fragrance, "Білий мускус", Drop, 15, 10, 200);

        Recipe R(CosmeticLine line, string name, string desc, decimal weight, int time, params (string Key, decimal Amount)[] items)
        {
            var r = new Recipe
            {
                Id = Guid.CreateVersion7(), Line = line, Name = name, Description = desc, Weight = weight, TimeMinutes = time,
                BatchSize = 1, CreatedAt = now, UpdatedAt = now, Version = Guid.NewGuid(),
            };
            r.Items = items.Select((x, i) => new RecipeItem
            {
                RecipeId = r.Id, IngredientId = ingredients[x.Key].Id, Amount = x.Amount, Position = i,
            }).ToList();
            return r;
        }

        db.Ingredients.AddRange(ingredients.Values);
        db.Recipes.AddRange(
            R(S, "Лавандове мило", "Ніжне мило на молоці з французькою лавандою та перламутровим відблиском.", 100, 40,
                ("m1", 1), ("b1", 100), ("e1", 12), ("p1", 3), ("o1", 1)),
            R(S, "Кавовий брусок-скраб", "Скрабуюче мило з меленою кавою — для рук після кухні.", 120, 35,
                ("m2", 1), ("b2", 120), ("f2", 15), ("o2", 1)),
            R(S, "Цитрусовий ранок", "Прозоре мило з апельсином і мідними іскрами.", 90, 30,
                ("m3", 1), ("b2", 90), ("e2", 14), ("p2", 1)),
            R(S, "Ромашка для малюків", "Без запашки, на козячому молоці з екстрактом ромашки.", 120, 45,
                ("m2", 1), ("b3", 120), ("x1", 3), ("o1", 1)),
            R(P, "Нічний жасмин", "Густий квітковий шлейф з мускусом.", 30, 20,
                ("v1", 1), ("pb1", 30), ("pf1", 40), ("pf4", 15)),
            R(P, "Бергамот і кедр", "Свіжий деревний роллер на жожобі.", 10, 15,
                ("v2", 1), ("pb2", 10), ("pf2", 12), ("pf3", 8)));
        await db.SaveChangesAsync(ct);
    }
}
