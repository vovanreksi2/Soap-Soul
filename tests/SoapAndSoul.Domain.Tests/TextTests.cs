using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Text;

namespace SoapAndSoul.Domain.Tests;

public class TextTests
{
    [Theory]
    [InlineData("лаванда", "Лавандове мило", true)]
    [InlineData("лавнда", "Лавандове мило", true)]   // one typo
    [InlineData("кава", "Кавовий брусок-скраб", true)]
    [InlineData("жасмин", "Лавандове мило", false)]
    [InlineData("", "anything", true)]
    public void Fuzzy_search(string query, string text, bool expected) =>
        Assert.Equal(expected, FuzzyMatcher.Matches(query, text));

    [Theory]
    [InlineData(0.184, "0,18")]
    [InlineData(4.5, "4,5")]
    [InlineData(156.4, "156")]
    [InlineData(1234, "1 234")]
    public void Numbers_use_ukrainian_format(double value, string expected) =>
        Assert.Equal(expected, Formatting.Number((decimal)value));

    [Fact]
    public void Batch_weight_switches_to_kilograms() =>
        Assert.Equal("1,2 кг", Formatting.Weight(1200, CosmeticLine.Soap));

    [Fact]
    public void Ukrainian_letters_sort_in_alphabet_order()
    {
        string[] names = ["Їжак", "Яблуко", "Ірис", "Ґудзик", "Гарбуз", "Євкаліпт", "Жасмин"];
        var sorted = names.OrderBy(n => n, UkrainianComparer.Instance).ToArray();
        Assert.Equal(["Гарбуз", "Ґудзик", "Євкаліпт", "Жасмин", "Ірис", "Їжак", "Яблуко"], sorted);
    }
}
