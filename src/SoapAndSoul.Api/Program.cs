using Microsoft.EntityFrameworkCore;
using SoapAndSoul.Api;
using SoapAndSoul.Api.Features;
using SoapAndSoul.Api.Images;
using SoapAndSoul.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddDatabase();
builder.Services.AddProblemDetails();
builder.Services.AddImageStorage(builder.Configuration);

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SoapAndSoulDbContext>();
    await db.Database.MigrateAsync();
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
api.MapFallback(() => Results.NotFound());
app.MapImageFiles();
app.MapGet("/healthz", () => Results.Ok("ok"));

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
