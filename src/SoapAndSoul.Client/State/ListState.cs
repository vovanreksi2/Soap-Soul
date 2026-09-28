using SoapAndSoul.Domain.Catalog;

namespace SoapAndSoul.Client.State;

public enum RecipeSort { Name, Cheapest, Priciest }

/// <summary>Search, sort and filter of the recipe list, kept per line while the app is open.</summary>
public sealed class ListState
{
    public sealed class Filters
    {
        public string Query { get; set; } = "";
        public RecipeSort Sort { get; set; } = RecipeSort.Name;
        public Guid? IngredientId { get; set; }
    }

    private readonly Dictionary<CosmeticLine, Filters> _filters = Lines.All.ToDictionary(l => l, _ => new Filters());

    public Filters For(CosmeticLine line) => _filters[line];

    public CosmeticLine LastLine { get; set; } = CosmeticLine.Soap;
}
