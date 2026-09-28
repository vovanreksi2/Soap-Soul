using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SoapAndSoul.Client;
using SoapAndSoul.Client.Services;
using SoapAndSoul.Client.State;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<HttpDataStore>();
builder.Services.AddScoped<IIngredientStore>(sp => sp.GetRequiredService<HttpDataStore>());
builder.Services.AddScoped<IRecipeStore>(sp => sp.GetRequiredService<HttpDataStore>());
builder.Services.AddScoped<IImageStore>(sp => sp.GetRequiredService<HttpDataStore>());
builder.Services.AddScoped<CatalogState>();
builder.Services.AddScoped<ListState>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<PhotoUploader>();

await builder.Build().RunAsync();
