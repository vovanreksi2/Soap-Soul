# CLAUDE.md

Guidance for Claude Code in this repository. UI text and domain terms are Ukrainian; code, comments and all documentation (README, docs) are English only.

## Commands

```bash
dotnet build                                   # whole solution (warnings are errors)
dotnet test                                    # Domain + API tests
dotnet run --project src/SoapAndSoul.Api       # app at http://localhost:5068 (API + Blazor client)
dotnet publish src/SoapAndSoul.Api -c Release -o out

dotnet tool restore
dotnet ef migrations add <Name> --project src/SoapAndSoul.Data   # uses DesignTimeFactory (SQLite)
```

Development seeds sample data into `src/SoapAndSoul.Api/App_Data/soapandsoul.db` (delete it to reset).
Migrations are applied at API startup.

## Architecture

- `SoapAndSoul.Domain` — shared by client and server; must stay free of EF/ASP.NET/Blazor references.
  - `Catalog/Categories.cs`: fixed categories per line (Soap/Perfume) with flags `Single`, `HasCapacity`, `Amortized`, `Required`.
  - `Selection/`: rule chain run when an ingredient is added (`SingleInCategoryRule` → `ExclusiveAromaRule` →
    `CapacityRule` → `BaseFollowsCapacityRule` → `DefaultAmountRule`). Add behavior as a new `ISelectionRule`.
  - `Costing/CostCalculator.cs`: drops = ml × 20; molds amortized over `UsesPerItem`.
  - `Validation/`: used by the API before saving and by the client before auto-save.
  - `Contracts/Dtos.cs`: wire format. Enums serialize as strings.
- `SoapAndSoul.Data` — EF Core, SQLite. Guid ids (client-generated, v7), `Version` concurrency token,
  soft delete via query filters. `Mapping.cs` maps entity ↔ DTO; recipes replace their item set from the DTO.
- `SoapAndSoul.Api` — minimal APIs in `Features/`. Upserts are `PUT /api/{ingredients|recipes}/{id}`;
  a stale `Version` returns `409` with the current copy. Deleting an ingredient removes it from recipes.
  Photos go through `IImageStorage` (`LocalImageStorage` → `App_Data/images`, served at `/images`).
  Client files are served by `MapStaticAssets()`; do not re-add `UseBlazorFrameworkFiles()` (it breaks `_framework` routing).
- `SoapAndSoul.Client` — Blazor WASM PWA.
  - `Services/`: `IRecipeStore`/`IIngredientStore`/`IImageStore` (HTTP implementation now; keep UI on these
    interfaces so an offline store can be added), `ToastService`, `PhotoUploader` (resizes in browser).
  - `State/`: `CatalogState` (cached data per line), `ListState` (search/sort/filter), `RecipeEditor`
    (working copy + debounced auto-save + conflict handling).
  - `Pages/`: `/{line}` list, `/{line}/recipes/{id}` recipe screen with slots and bottom sheets.
  - Styles: `wwwroot/css/nocturne.css` (design-system tokens, don't edit casually) + `wwwroot/css/app.css`.
    Use Nocturne variables, Phosphor icons (`ph ph-*`), no hard-coded colors.
  - `InvariantGlobalization` is on: format numbers with `Domain/Text/Formatting`, sort with `UkrainianComparer`.
  - `index.html` must reference `_framework/blazor.webassembly.js` without fingerprint placeholders.

Design reference: `docs/design/soap-and-soul-mobile-v2.html` (the prototype this app implements).
