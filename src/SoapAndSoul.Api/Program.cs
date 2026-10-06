using Microsoft.EntityFrameworkCore;
using SoapAndSoul.Api;
using SoapAndSoul.Api.Drafts;
using SoapAndSoul.Api.Features;
using SoapAndSoul.Api.Images;
using SoapAndSoul.Api.Llm;
using SoapAndSoul.Api.Mcp;
using SoapAndSoul.Api.Services;
using SoapAndSoul.Data;
using SoapAndSoul.Data.Catalog;

var builder = WebApplication.CreateBuilder(args);

builder.AddKeyVaultSecrets();
builder.AddDatabase();
builder.Services.AddProblemDetails();
builder.Services.AddImageStorage(builder.Configuration);
builder.Services.AddScoped<IngredientService>();
builder.Services.AddScoped<RecipeService>();
builder.Services.AddLlm(builder.Configuration);
builder.Services.AddScoped<RecipeDraftBuilder>();
builder.AddSoapAndSoulMcp();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SoapAndSoulDbContext>();
    await db.Database.MigrateAsync();
    await app.ImportIfConfiguredAsync(db);
    if (app.Configuration.GetValue<bool>("Database:SeedCatalog"))
        await CatalogSeed.SeedAsync(db);
    if (app.Configuration.GetValue<bool>("Database:SeedSampleData"))
        await SeedData.SeedIfEmptyAsync(db);
}

app.UseExceptionHandler();
if (app.Environment.IsDevelopment()) app.UseWebAssemblyDebugging();

app.UseStaticFiles();
// Serves the client's _framework files by their plain names (fingerprinted and Brotli-compressed on disk).
app.MapStaticAssets();

var api = app.MapGroup("/api");
api.MapIngredientEndpoints();
api.MapRecipeEndpoints();
api.MapImageEndpoints();
api.MapDraftEndpoints();
api.MapFallback(() => Results.NotFound());
app.MapImageFiles();
app.MapSoapAndSoulMcp();
app.MapGet("/healthz", () => Results.Ok("ok"));

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
