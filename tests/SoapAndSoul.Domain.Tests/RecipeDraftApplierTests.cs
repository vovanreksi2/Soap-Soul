using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;
using SoapAndSoul.Domain.Drafts;
using static SoapAndSoul.Domain.Tests.TestData;

namespace SoapAndSoul.Domain.Tests;

public class RecipeDraftApplierTests
{
    private static RecipeDto Recipe(params RecipeItemDto[] items) =>
        new(Guid.NewGuid(), CosmeticLine.Soap, "Без назви", "", 0, 0, 1, null, items);

    private static RecipeDraftDto Draft(params RecipeDraftItemDto[] items) => new(null, null, null, null, null, items, []);

    private static RecipeDraftItemDto Item(IngredientDto? ing, decimal? amount = null, MeasureUnit? unit = null, string spoken = "щось") =>
        new(spoken, ing?.Id, ing?.Category, amount, unit);

    [Fact]
    public void Ingredients_go_through_the_selection_rules()
    {
        var mold = Ing(CategoryKey.Mold, MeasureUnit.Piece, capacity: 120);
        var soapBase = Ing(CategoryKey.SoapBase, MeasureUnit.Gram, typical: 100);
        var lookup = Lookup(mold, soapBase);

        var result = RecipeDraftApplier.Apply(Recipe(), Draft(Item(soapBase), Item(mold)), lookup);

        Assert.Equal(120, result.Recipe.Weight);
        Assert.Equal([new RecipeItemDto(soapBase.Id, 120), new RecipeItemDto(mold.Id, 1)], result.Recipe.Items);
    }

    [Fact]
    public void Dictated_millilitres_are_converted_to_drops()
    {
        var oil = Ing(CategoryKey.EssentialOil, MeasureUnit.Drop, typical: 5);

        var result = RecipeDraftApplier.Apply(Recipe(), Draft(Item(oil, 1.5m, MeasureUnit.Milliliter)), Lookup(oil));

        Assert.Equal([new RecipeItemDto(oil.Id, 30)], result.Recipe.Items);
    }

    [Fact]
    public void Unconvertible_unit_keeps_typical_amount_with_a_notice()
    {
        var pigment = Ing(CategoryKey.Pigment, MeasureUnit.Drop, typical: 3);

        var result = RecipeDraftApplier.Apply(Recipe(), Draft(Item(pigment, 2, MeasureUnit.Gram)), Lookup(pigment));

        Assert.Equal([new RecipeItemDto(pigment.Id, 3)], result.Recipe.Items);
        Assert.Single(result.Notices);
    }

    [Fact]
    public void Ingredient_already_in_recipe_only_gets_the_new_amount()
    {
        var pigment = Ing(CategoryKey.Pigment, MeasureUnit.Drop, typical: 3);

        var result = RecipeDraftApplier.Apply(Recipe(new RecipeItemDto(pigment.Id, 3)), Draft(Item(pigment, 7)), Lookup(pigment));

        Assert.Equal([new RecipeItemDto(pigment.Id, 7)], result.Recipe.Items);
    }

    [Fact]
    public void Unknown_or_foreign_ingredients_are_reported_not_added()
    {
        var perfumeBase = Ing(CategoryKey.PerfumeBase, MeasureUnit.Milliliter, line: CosmeticLine.Perfume);

        var result = RecipeDraftApplier.Apply(Recipe(),
            Draft(Item(null, spoken: "мед"), Item(perfumeBase, spoken: "спирт")), Lookup(perfumeBase));

        Assert.Empty(result.Recipe.Items);
        Assert.Equal(["мед", "спирт"], result.Unmatched);
    }

    [Fact]
    public void Only_dictated_fields_change_and_description_is_appended()
    {
        var recipe = Recipe() with { Name = "Лаванда", Description = "Крок 1.", TimeMinutes = 30, BatchSize = 2 };
        var draft = new RecipeDraftDto(null, "Крок 2.", null, 45, null, [], []);

        var result = RecipeDraftApplier.Apply(recipe, draft, Lookup()).Recipe;

        Assert.Equal("Лаванда", result.Name);
        Assert.Equal("Крок 1.\nКрок 2.", result.Description);
        Assert.Equal(45, result.TimeMinutes);
        Assert.Equal(2, result.BatchSize);
    }

    [Theory]
    [InlineData(MeasureUnit.Milliliter, MeasureUnit.Drop, 2, 40)]
    [InlineData(MeasureUnit.Drop, MeasureUnit.Milliliter, 10, 0.5)]
    [InlineData(MeasureUnit.Gram, MeasureUnit.Gram, 7, 7)]
    public void Units_convert_between_ml_and_drops(MeasureUnit from, MeasureUnit to, decimal amount, decimal expected) =>
        Assert.Equal(expected, Units.Convert(amount, from, to));

    [Fact]
    public void Grams_do_not_convert_to_drops() =>
        Assert.Null(Units.Convert(1, MeasureUnit.Gram, MeasureUnit.Drop));
}
