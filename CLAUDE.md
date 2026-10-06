# CLAUDE.md

Guidance for Claude Code in this repository. UI text and domain terms are Ukrainian; code, comments and all documentation (README, docs) are English only.

## Commands

```bash
dotnet build                                   # whole solution (warnings are errors)
dotnet test                                    # Domain + API + UI tests
pwsh tests/SoapAndSoul.UI.Tests/bin/Debug/net10.0/playwright.ps1 install chromium   # once, after a build
dotnet run --project src/SoapAndSoul.Api       # app at http://localhost:5068 (API + Blazor client)
dotnet publish src/SoapAndSoul.Api -c Release -o out

dotnet tool restore
dotnet ef migrations add <Name> --project src/SoapAndSoul.Data             # SQLite (development)
dotnet ef migrations add <Name> --project src/SoapAndSoul.Data.SqlServer   # Azure SQL (production)
```

Model changes need a migration in both projects; `MigrationTests` fails otherwise.

Tests: `ApiFactory` hosts the app on a named in-memory SQLite database (`InMemoryDatabase`, real migrations);
tests share a database per class, so use `TestCatalog.Unique` names. `SoapAndSoul.UI.Tests` reuses that factory
on a Kestrel port and drives Chromium via Playwright (`HEADED=1` to watch); it links the shared files from the API tests.

Development seeds sample data into `src/SoapAndSoul.Api/App_Data/soapandsoul.db` (delete it to reset).
All environments seed the supplier catalog (`Data/Catalog/CatalogSeed.cs` + embedded `aromasoap.json`, `Database:SeedCatalog`;
ids derive from the product URL, so only never-seen entries are inserted). Catalog entries are a reference
(`InStock = false`); the picker shows "В наявності" and "Довідник" tabs, user-created ingredients start in stock. Rebuild the JSON with `python tools/aromasoap/scrape.py`.
Migrations are applied at API startup. Temporary: `Database:ImportFrom` (production) copies the old Basic-tier
Azure SQL database into the new free-offer one when it is empty (`Data/DatabaseImport.cs`); remove it with
`sqlLegacyDatabase` in `infra/main.bicep` once the move is confirmed (`docs/deploy/azure.md`).

## Architecture

- `SoapAndSoul.Domain` — shared by client and server; must stay free of EF/ASP.NET/Blazor references.
  - `Catalog/Categories.cs`: fixed categories per line (Soap/Perfume) with flags `Single`, `HasCapacity`, `Amortized`, `Required`.
  - `Selection/`: rule chain run when an ingredient is added (`SingleInCategoryRule` → `ExclusiveAromaRule` →
    `CapacityRule` → `BaseFollowsCapacityRule` → `DefaultAmountRule`). Add behavior as a new `ISelectionRule`.
  - `Costing/CostCalculator.cs`: drops = ml × 20; molds amortized over `UsesPerItem`.
  - `Validation/`: used by the API before saving and by the client before auto-save.
  - `Drafts/RecipeDraftApplier.cs`: merges a voice draft into a recipe through the selection rules (ml ↔ drops via `Units.Convert`).
  - `Contracts/Dtos.cs`: wire format. Enums serialize as strings.
- `SoapAndSoul.Data` — EF Core; provider chosen by `Database:Provider` (`Sqlite` in development and tests,
  `SqlServer` in production, migrations in `SoapAndSoul.Data.SqlServer`). Guid ids (client-generated, v7), `Version` concurrency token,
  soft delete via query filters. `Mapping.cs` maps entity ↔ DTO; recipes replace their item set from the DTO.
- `SoapAndSoul.Api` — minimal APIs in `Features/`, thin over `Services/` (`IngredientService`, `RecipeService`
  return `SaveOutcome<T>`), which the MCP tools share. Upserts are `PUT /api/{ingredients|recipes}/{id}`;
  a stale `Version` returns `409` with the current copy. Deleting an ingredient removes it from recipes.
  - `Mcp/`: MCP server at `/mcp` (Streamable HTTP, stateless; `Mcp:ApiKey` bearer, or `Mcp:AllowAnonymous` in
    development; neither → off). Tools edit whole
    documents through the services and `SelectionEngine`; errors are `McpException`s with readable messages.
  - `Llm/`: `ILlmClient` (JSON-schema structured output), provider by `Llm:Provider` (`None`, `Anthropic` via the
    official SDK, `claude-opus-5-5`, `fallbacks: "default"`). Add providers there, not in features.
  - `Drafts/RecipeDraftBuilder.cs` + `POST /api/recipe-drafts`: dictated text + catalog → `RecipeDraftDto`;
    ids not in the line's catalog are dropped. `GET /api/features` tells the client whether voice is available.
  Photos go through `IImageStorage` (`Images:Provider`: `Local` → `App_Data/images`, `AzureBlob` → private
  container via managed identity); both are streamed by the app at `/images/{name}`.
  Client files are served by `MapStaticAssets()`; do not re-add `UseBlazorFrameworkFiles()` (it breaks `_framework` routing).
- `SoapAndSoul.Client` — Blazor WASM PWA.
  - `Services/`: `IRecipeStore`/`IIngredientStore`/`IImageStore`/`IRecipeDraftService` (HTTP implementation now; keep UI on these
    interfaces so an offline store can be added), `ToastService`, `PhotoUploader` (resizes in browser),
    `ISpeechRecognizer` (Web Speech API in `js/app.js`, on-device when the browser allows).
  - `State/`: `CatalogState` (cached data per line), `ListState` (search/sort/filter), `RecipeEditor`
    (working copy + debounced auto-save + conflict handling).
  - `Pages/`: `/{line}` list, `/{line}/recipes/{id}` recipe screen with slots and bottom sheets
    (`VoiceSheet`: dictate → review → apply).
  - Styles: `wwwroot/css/nocturne.css` (design-system tokens, don't edit casually) + `wwwroot/css/app.css`.
    Use Nocturne variables, Phosphor icons (`ph ph-*`), no hard-coded colors.
  - `InvariantGlobalization` is on: format numbers with `Domain/Text/Formatting`, sort with `UkrainianComparer`.
  - `index.html` must reference `_framework/blazor.webassembly.js` without fingerprint placeholders.

Deployment: `infra/main.bicep` (App Service + Azure SQL + Blob Storage + Key Vault, one user-assigned managed identity)
is deployed by `.github/workflows/deploy.yml` before the app on every push to `main`. API keys live only in Key Vault
(`KeyVault:Uri` → loaded into configuration at startup by `KeyVault.cs`; secret `Llm--ApiKey` = `Llm:ApiKey`) and are
set by hand with `infra/set-secrets.sh`, never by the template or pipeline. Setup in `docs/deploy/azure.md`.

Design reference: `docs/design/soap-and-soul-mobile-v2.html` (the prototype this app implements).
