using Microsoft.EntityFrameworkCore;
using SoapAndSoul.Api;
using SoapAndSoul.Api.Features;
using SoapAndSoul.Api.Images;
using SoapAndSoul.Data;

var builder = WebApplication.CreateBuilder(args);

var connectionString = SqlitePaths.Resolve(
    builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured."),
    builder.Environment.ContentRootPath);
builder.Services.AddDbContext<SoapAndSoulDbContext>(o => o.UseSqlite(connectionString));
builder.Services.AddProblemDetails();
builder.Services.Configure<ImageStorageOptions>(builder.Configuration.GetSection(ImageStorageOptions.Section));
builder.Services.AddSingleton<IImageStorage, LocalImageStorage>();

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

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseImageFiles();

var api = app.MapGroup("/api");
api.MapIngredientEndpoints();
api.MapRecipeEndpoints();
api.MapImageEndpoints();
api.MapFallback(() => Results.NotFound());
app.MapGet("/healthz", () => Results.Ok("ok"));

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
