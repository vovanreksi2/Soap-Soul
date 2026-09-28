using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;
using SoapAndSoul.Domain.Selection;
using static SoapAndSoul.Domain.Tests.TestData;

namespace SoapAndSoul.Domain.Tests;

public class SelectionEngineTests
{
    private readonly SelectionEngine _engine = SelectionEngine.Default;

    [Fact]
    public void New_pick_starts_at_typical_amount()
    {
        var pigment = Ing(CategoryKey.Pigment, MeasureUnit.Drop, typical: 3);

        var result = _engine.Toggle(CosmeticLine.Soap, [], 0, pigment, Lookup(pigment));

        Assert.True(result.Added);
        Assert.Equal([new RecipeItemDto(pigment.Id, 3)], result.Items);
        Assert.False(result.ClosePicker);
    }

    [Fact]
    public void Toggling_a_selected_ingredient_removes_it()
    {
        var pigment = Ing(CategoryKey.Pigment, MeasureUnit.Drop);

        var result = _engine.Toggle(CosmeticLine.Soap, [new(pigment.Id, 3)], 0, pigment, Lookup(pigment));

        Assert.False(result.Added);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void Mold_replaces_previous_mold_sets_weight_and_resizes_base()
    {
        var oldMold = Ing(CategoryKey.Mold, MeasureUnit.Piece, capacity: 100);
        var newMold = Ing(CategoryKey.Mold, MeasureUnit.Piece, capacity: 120);
        var soapBase = Ing(CategoryKey.SoapBase, MeasureUnit.Gram, typical: 100);
        RecipeItemDto[] items = [new(oldMold.Id, 1), new(soapBase.Id, 100)];

        var result = _engine.Toggle(CosmeticLine.Soap, items, 100, newMold, Lookup(oldMold, newMold, soapBase));

        Assert.Equal(120, result.Weight);
        Assert.Equal([new RecipeItemDto(soapBase.Id, 120), new RecipeItemDto(newMold.Id, 1)], result.Items);
        Assert.True(result.ClosePicker);
    }

    [Fact]
    public void Base_fills_the_chosen_mold()
    {
        var mold = Ing(CategoryKey.Mold, MeasureUnit.Piece, capacity: 90);
        var soapBase = Ing(CategoryKey.SoapBase, MeasureUnit.Gram, typical: 100);

        var result = _engine.Toggle(CosmeticLine.Soap, [new(mold.Id, 1)], 90, soapBase, Lookup(mold, soapBase));

        Assert.Contains(new RecipeItemDto(soapBase.Id, 90), result.Items);
    }

    [Fact]
    public void Base_without_mold_uses_typical_amount()
    {
        var soapBase = Ing(CategoryKey.SoapBase, MeasureUnit.Gram, typical: 100);

        var result = _engine.Toggle(CosmeticLine.Soap, [], 0, soapBase, Lookup(soapBase));

        Assert.Equal([new RecipeItemDto(soapBase.Id, 100)], result.Items);
    }

    [Fact]
    public void Essential_oil_removes_fragrances_with_a_notice()
    {
        var fragrance = Ing(CategoryKey.Fragrance, MeasureUnit.Drop, typical: 15);
        var oil = Ing(CategoryKey.EssentialOil, MeasureUnit.Drop, typical: 12);

        var result = _engine.Toggle(CosmeticLine.Soap, [new(fragrance.Id, 15)], 0, oil, Lookup(fragrance, oil));

        Assert.Equal([new RecipeItemDto(oil.Id, 12)], result.Items);
        Assert.Equal(["Запашки прибрано"], result.Notices);
    }

    [Fact]
    public void Perfume_fragrances_have_no_exclusivity()
    {
        var a = Ing(CategoryKey.Fragrance, MeasureUnit.Drop, typical: 10, line: CosmeticLine.Perfume);
        var b = Ing(CategoryKey.Fragrance, MeasureUnit.Drop, typical: 5, line: CosmeticLine.Perfume);

        var result = _engine.Toggle(CosmeticLine.Perfume, [new(a.Id, 10)], 0, b, Lookup(a, b));

        Assert.Equal(2, result.Items.Count);
        Assert.Empty(result.Notices);
    }

    [Fact]
    public void SetAmount_never_goes_negative()
    {
        var id = Guid.NewGuid();
        var items = SelectionEngine.SetAmount([new(id, 3)], id, -5);
        Assert.Equal(0, items[0].Amount);
    }
}
