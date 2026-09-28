using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;
using SoapAndSoul.Domain.Costing;
using static SoapAndSoul.Domain.Tests.TestData;

namespace SoapAndSoul.Domain.Tests;

public class CostCalculatorTests
{
    [Fact]
    public void Grams_are_price_over_quantity() =>
        Assert.Equal(0.42m, CostCalculator.UnitPrice(MeasureUnit.Gram, 1000, 420));

    [Fact]
    public void Drops_are_bought_in_millilitres_at_20_drops_per_ml() =>
        Assert.Equal(1.05m, CostCalculator.UnitPrice(MeasureUnit.Drop, 10, 210));

    [Fact]
    public void Reusable_mold_is_amortized_over_its_uses() =>
        Assert.Equal(1.8m, CostCalculator.UnitPrice(MeasureUnit.Piece, 1, 180, usesPerItem: 100));

    [Fact]
    public void Missing_purchase_data_costs_nothing() =>
        Assert.Equal(0, CostCalculator.UnitPrice(MeasureUnit.Gram, 0, 100));

    [Fact]
    public void Recipe_cost_sums_items_and_skips_deleted_ingredients()
    {
        var mold = Ing(CategoryKey.Mold, MeasureUnit.Piece, price: 180, capacity: 100, uses: 100);
        var soapBase = Ing(CategoryKey.SoapBase, MeasureUnit.Gram, qty: 1000, price: 420);
        RecipeItemDto[] items = [new(mold.Id, 1), new(soapBase.Id, 100), new(Guid.NewGuid(), 5)];

        Assert.Equal(1.8m + 42m, CostCalculator.RecipeCost(items, Lookup(mold, soapBase)));
    }
}
