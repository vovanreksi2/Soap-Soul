using Microsoft.EntityFrameworkCore;
using SoapAndSoul.Data.Catalog;
using SoapAndSoul.Data.Entities;
using SoapAndSoul.Domain.Catalog;
using static SoapAndSoul.Domain.Catalog.CategoryKey;
using static SoapAndSoul.Domain.Catalog.MeasureUnit;

namespace SoapAndSoul.Data;

/// <summary>
/// Sample recipes for a fresh development database. Soap recipes use the supplier catalog
/// (<see cref="CatalogSeed"/>); the categories it lacks and the perfume line get sample ingredients.
/// </summary>
public static class SeedData
{
    public static async Task SeedIfEmptyAsync(SoapAndSoulDbContext db, CancellationToken ct = default)
    {
        await CatalogSeed.SeedAsync(db, ct);
        if (await db.Recipes.IgnoreQueryFilters().AnyAsync(ct)) return;

        var now = DateTimeOffset.UtcNow;
        var ingredients = new Dictionary<string, Ingredient>();
        var ids = new Dictionary<string, Guid>();

        void I(string key, CosmeticLine line, CategoryKey cat, string name, MeasureUnit unit, decimal typical,
            decimal qty, decimal price, decimal? capacity = null, int uses = 1) =>
            ingredients[key] = new Ingredient
            {
                Id = Guid.CreateVersion7(), Line = line, Category = cat, Name = name, Unit = unit, TypicalAmount = typical,
                PurchaseQuantity = qty, PurchasePrice = price, Capacity = capacity, UsesPerItem = uses,
                CreatedAt = now, UpdatedAt = now, Version = Guid.NewGuid(),
            };

        void C(string key, CategoryKey cat, string name) => ids[key] = CatalogSeed.Get(cat, name).Id;

        const CosmeticLine S = CosmeticLine.Soap, P = CosmeticLine.Perfume;
        C("m1", Mold, "Лаванда 71г форма пластикова");
        C("m2", Mold, "Прямокутник 84 г форма пластикова");
        C("m3", Mold, "Прямокутник міні 54 г форма пластикова");
        C("b1", SoapBase, "Мильна основа Neri Ultra White біла, Україна");
        C("b2", SoapBase, "Мильна основа Neri Ultra прозора, Україна");
        C("b3", SoapBase, "Мильна основа Velona Goats Milk sls free біла 1 кг");
        C("f2", Fragrance, "Ароматна кава");
        C("p1", Pigment, "Рідкий барвник фіолетовий");
        C("p2", Pigment, "Пігмент перламутровий мідний");
        C("x1", Extract, "Рідкий екстракт квіток Ромашки гліколевий");
        I("e1", S, EssentialOil, "Лаванда французька", Drop, 12, 10, 210);
        I("e2", S, EssentialOil, "Апельсин солодкий", Drop, 14, 10, 120);
        I("e3", S, EssentialOil, "Евкаліпт", Drop, 10, 10, 140);
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
                RecipeId = r.Id, IngredientId = ids.TryGetValue(x.Key, out var id) ? id : ingredients[x.Key].Id, Amount = x.Amount, Position = i,
            }).ToList();
            return r;
        }

        // The catalog entries the samples use are ones the user owns.
        var used = ids.Values.ToList();
        await db.Ingredients.Where(i => used.Contains(i.Id)).ForEachAsync(i => i.InStock = true, ct);

        db.Ingredients.AddRange(ingredients.Values);
        db.Recipes.AddRange(
            R(S, "Лавандове мило", "Ніжне біле мило з французькою лавандою та фіолетовими розводами.", 71, 40,
                ("m1", 1), ("b1", 71), ("e1", 12), ("p1", 3), ("o1", 1)),
            R(S, "Кавовий брусок-скраб", "Скрабуюче мило з меленою кавою — для рук після кухні.", 84, 35,
                ("m2", 1), ("b2", 84), ("f2", 15), ("o2", 1)),
            R(S, "Цитрусовий ранок", "Прозоре мило з апельсином і мідними іскрами.", 54, 30,
                ("m3", 1), ("b2", 54), ("e2", 14), ("p2", 1)),
            R(S, "Ромашка для малюків", "Без запашки, на козячому молоці з екстрактом ромашки.", 84, 45,
                ("m2", 1), ("b3", 84), ("x1", 3), ("o1", 1)),
            R(P, "Нічний жасмин", "Густий квітковий шлейф з мускусом.", 30, 20,
                ("v1", 1), ("pb1", 30), ("pf1", 40), ("pf4", 15)),
            R(P, "Бергамот і кедр", "Свіжий деревний роллер на жожобі.", 10, 15,
                ("v2", 1), ("pb2", 10), ("pf2", 12), ("pf3", 8)));
        await db.SaveChangesAsync(ct);
    }
}
