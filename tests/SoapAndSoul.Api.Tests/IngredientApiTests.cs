using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;
using static SoapAndSoul.Api.Tests.TestCatalog;

namespace SoapAndSoul.Api.Tests;

public class IngredientApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _http = factory.CreateClient();

    private async Task<List<IngredientDto>> ListAsync(CosmeticLine line) =>
        (await _http.GetFromJsonAsync<List<IngredientDto>>($"/api/ingredients?line={line}"))!;

    [Fact]
    public async Task New_ingredient_is_created_then_updated_with_a_new_version()
    {
        var mold = Mold(Unique("  Кругла  "));

        var created = await _http.PutAsJsonAsync($"/api/ingredients/{mold.Id}", mold);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal($"/api/ingredients/{mold.Id}", created.Headers.Location?.OriginalString);
        var first = (await created.Content.ReadFromJsonAsync<IngredientDto>())!;
        Assert.Equal(mold.Name.Trim(), first.Name);
        Assert.NotNull(first.Version);

        var updated = await _http.PutAsJsonAsync($"/api/ingredients/{mold.Id}", first with { PurchasePrice = 250 });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var second = (await updated.Content.ReadFromJsonAsync<IngredientDto>())!;
        Assert.Equal(250, second.PurchasePrice);
        Assert.NotEqual(first.Version, second.Version);

        Assert.Equal(second, (await ListAsync(CosmeticLine.Soap)).Single(i => i.Id == mold.Id));
    }

    [Fact]
    public async Task List_contains_only_the_requested_line_ordered_by_category_then_name()
    {
        var suffix = Unique("");
        var soapBase = await _http.SaveAsync(SoapBase("Ялиця" + suffix));
        var moldB = await _http.SaveAsync(Mold("Бджілка" + suffix));
        var moldA = await _http.SaveAsync(Mold("Абрикос" + suffix));
        var bottle = await _http.SaveAsync(Bottle("Флакон" + suffix));

        var soap = (await ListAsync(CosmeticLine.Soap)).Where(i => i.Name.EndsWith(suffix)).Select(i => i.Id);
        var perfume = (await ListAsync(CosmeticLine.Perfume)).Where(i => i.Name.EndsWith(suffix)).Select(i => i.Id);

        Assert.Equal([moldA.Id, moldB.Id, soapBase.Id], soap);
        Assert.Equal([bottle.Id], perfume);
    }

    [Fact]
    public async Task Stale_version_is_rejected_with_the_current_copy()
    {
        var first = await _http.SaveAsync(Pigment(Unique("Мідь")));
        var second = await _http.SaveAsync(first with { Name = "Мідь нова" });

        var res = await _http.PutAsJsonAsync($"/api/ingredients/{first.Id}", first with { Name = "Застаріла" });

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal(second, await res.Content.ReadFromJsonAsync<IngredientDto>());
    }

    [Fact]
    public async Task Concurrent_saves_of_the_same_version_let_only_one_win()
    {
        var saved = await _http.SaveAsync(Pigment(Unique("Охра")));

        var results = await Task.WhenAll(Enumerable.Range(1, 2).Select(n =>
            _http.PutAsJsonAsync($"/api/ingredients/{saved.Id}", saved with { Name = $"Охра {n}" })));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Invalid_ingredient_returns_the_problem_fields()
    {
        var bad = Mold(" ") with { Unit = MeasureUnit.Gram, Capacity = 0, PurchasePrice = 0 };

        var res = await _http.PutAsJsonAsync($"/api/ingredients/{bad.Id}", bad);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var problem = (await res.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
        Assert.Equal(["Capacity", "Name", "PurchasePrice", "Unit"], problem.Errors.Keys.Order());
    }

    [Fact]
    public async Task Category_from_another_line_is_rejected()
    {
        var bottleInSoap = Bottle() with { Line = CosmeticLine.Soap };

        var res = await _http.PutAsJsonAsync($"/api/ingredients/{bottleInSoap.Id}", bottleInSoap);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Category_of_a_saved_ingredient_cannot_change()
    {
        var fragrance = await _http.SaveAsync(Fragrance(name: Unique("Ваніль")));

        var res = await _http.PutAsJsonAsync($"/api/ingredients/{fragrance.Id}", fragrance with { Category = CategoryKey.EssentialOil });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Id_in_the_path_must_match_the_body()
    {
        var res = await _http.PutAsJsonAsync($"/api/ingredients/{Guid.NewGuid()}", Pigment());

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Deleted_ingredient_stays_in_the_database_but_cannot_be_saved_again()
    {
        var oil = await _http.SaveAsync(EssentialOil(Unique("Евкаліпт")));

        Assert.Equal(HttpStatusCode.NoContent, (await _http.DeleteAsync($"/api/ingredients/{oil.Id}")).StatusCode);

        var row = await factory.WithDbAsync(db => db.Ingredients.IgnoreQueryFilters().SingleAsync(i => i.Id == oil.Id));
        Assert.True(row.IsDeleted);
        Assert.DoesNotContain(await ListAsync(CosmeticLine.Soap), i => i.Id == oil.Id);
        var res = await _http.PutAsJsonAsync($"/api/ingredients/{oil.Id}", oil with { Version = row.Version });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Deleting_an_unknown_ingredient_is_harmless() =>
        Assert.Equal(HttpStatusCode.NoContent, (await _http.DeleteAsync($"/api/ingredients/{Guid.NewGuid()}")).StatusCode);
}
