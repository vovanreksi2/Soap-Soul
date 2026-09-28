using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;
using SoapAndSoul.Domain.Validation;
using static SoapAndSoul.Domain.Tests.TestData;

namespace SoapAndSoul.Domain.Tests;

public class ValidationTests
{
    private static RecipeDto Recipe(params RecipeItemDto[] items) =>
        new(Guid.NewGuid(), CosmeticLine.Soap, "Мило", "", 100, 30, 1, null, items);

    [Fact]
    public void Valid_ingredient_passes() =>
        Assert.True(IngredientValidator.Validate(Ing(CategoryKey.Pigment, MeasureUnit.Drop, typical: 3, qty: 10, price: 90)).IsValid);

    [Fact]
    public void Mold_needs_capacity()
    {
        var errors = IngredientValidator.Validate(Ing(CategoryKey.Mold, MeasureUnit.Piece, capacity: null));
        Assert.Contains(nameof(IngredientDto.Capacity), errors.Keys);
    }

    [Fact]
    public void Unit_must_belong_to_category()
    {
        var errors = IngredientValidator.Validate(Ing(CategoryKey.SoapBase, MeasureUnit.Drop, typical: 100, qty: 1000));
        Assert.Contains(nameof(IngredientDto.Unit), errors.Keys);
    }

    [Fact]
    public void Category_from_other_line_is_rejected()
    {
        var errors = IngredientValidator.Validate(Ing(CategoryKey.Bottle, MeasureUnit.Piece, capacity: 30));
        Assert.Contains(nameof(IngredientDto.Category), errors.Keys);
    }

    [Fact]
    public void Recipe_rejects_two_molds_and_mixed_aromas()
    {
        var m1 = Ing(CategoryKey.Mold, MeasureUnit.Piece, capacity: 100);
        var m2 = Ing(CategoryKey.Mold, MeasureUnit.Piece, capacity: 120);
        var f = Ing(CategoryKey.Fragrance, MeasureUnit.Drop);
        var e = Ing(CategoryKey.EssentialOil, MeasureUnit.Drop);

        var errors = RecipeValidator.Validate(Recipe(new(m1.Id, 1), new(m2.Id, 1), new(f.Id, 1), new(e.Id, 1)), Lookup(m1, m2, f, e));

        Assert.Equal(2, errors[nameof(RecipeDto.Items)].Length);
    }

    [Fact]
    public void Recipe_rejects_unknown_and_foreign_ingredients()
    {
        var bottle = Ing(CategoryKey.Bottle, MeasureUnit.Piece, capacity: 30, line: CosmeticLine.Perfume);

        var errors = RecipeValidator.Validate(Recipe(new(bottle.Id, 1), new(Guid.NewGuid(), 1)), Lookup(bottle));

        Assert.Equal(2, errors[nameof(RecipeDto.Items)].Length);
    }
}
