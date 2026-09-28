using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;

namespace SoapAndSoul.Domain.Tests;

internal static class TestData
{
    public static IngredientDto Ing(CategoryKey cat, MeasureUnit unit, decimal typical = 1, decimal qty = 1, decimal price = 100,
        decimal? capacity = null, int uses = 1, CosmeticLine line = CosmeticLine.Soap) =>
        new(Guid.NewGuid(), line, cat, cat.ToString(), unit, typical, qty, price, capacity, uses, null);

    public static Func<Guid, IngredientDto?> Lookup(params IngredientDto[] all) =>
        id => all.FirstOrDefault(i => i.Id == id);
}
