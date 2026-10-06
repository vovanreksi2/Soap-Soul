# Soap & Soul

A PWA for soap and perfume recipes with cost calculation. It runs in the browser and can be installed as an app
on Android (as well as iPhone and Windows).

## Getting started

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/SoapAndSoul.Api
```

Open http://localhost:5068. In development the SQLite database (`src/SoapAndSoul.Api/App_Data/soapandsoul.db`)
is created automatically and seeded with sample data. Delete the file to start from scratch.

Every environment (production included) seeds the supplier catalog from aromasoap.com.ua — molds, soap bases,
colors, fragrances and extracts with prices, capacities and photos — from `src/SoapAndSoul.Data/Catalog/aromasoap.json`
(`Database:SeedCatalog`). Entries are only ever added: ones you edit or delete are left alone. To refresh the catalog,
run `python tools/aromasoap/scrape.py` (standard library only; pages are cached in `tools/aromasoap/.cache`).

```bash
dotnet build                                           # once, before installing the browser
pwsh tests/SoapAndSoul.UI.Tests/bin/Debug/net10.0/playwright.ps1 install chromium   # once, for UI tests
dotnet test                                            # domain, API integration and UI tests
dotnet publish src/SoapAndSoul.Api -c Release -o out   # server build
```

### Testing on a phone

A PWA can only be installed over HTTPS (or from `localhost`). The simplest options:
- Android + USB: `chrome://inspect` → Port forwarding `5068 → localhost:5068`, then open `http://localhost:5068` on the phone;
- or a tunnel (`dev tunnels`, `ngrok`) to port 5068.

In Chrome on Android: menu ⋮ → "Install app".

## MCP server

The API also hosts an [MCP](https://modelcontextprotocol.io) server at `/mcp` (Streamable HTTP, stateless), so an
assistant such as Claude can work with the same data as the app. Tools:

| Tool | Does |
|---|---|
| `list_categories` | Categories of a line with their rules (units, required, single, capacity, amortized). |
| `list_ingredients`, `create_ingredient`, `update_ingredient`, `delete_ingredient` | The ingredient catalog; results include the unit price. |
| `list_recipes`, `get_recipe` | Recipes with cost per piece and per batch, item costs, missing required categories. |
| `create_recipe`, `update_recipe`, `delete_recipe` | Recipe fields. |
| `add_recipe_ingredient`, `set_recipe_ingredient_amount`, `remove_recipe_ingredient` | Composition, through the same selection rules as the app (one mold, base follows the mold, fragrances vs essential oils). |

Connect Claude Code to a local instance:

```bash
claude mcp add --transport http soap-and-soul http://localhost:5068/mcp
```

Settings: `Mcp:Enabled` (default `true`), `Mcp:ApiKey` and `Mcp:AllowAnonymous`. With a key, clients must send
`Authorization: Bearer <key>` (`claude mcp add ... --header "Authorization: Bearer <key>"`). Without a key the
endpoint is served only when `Mcp:AllowAnonymous` is `true` (development does that); otherwise `/mcp` is off.

## Voice input

On the recipe screen, the microphone button opens a sheet where the recipe can be dictated in Ukrainian:

1. **Speech → text on the device.** The browser's Web Speech API, with on-device processing requested where the
   browser supports it (Chrome's `processLocally`; the sheet shows "на пристрої"). Otherwise the browser's default
   engine is used. The text stays editable, and the keyboard's own dictation works too. The engine sits behind
   `ISpeechRecognizer`, so it can be replaced (e.g. Whisper in WebAssembly).
2. **Text → draft on the server.** `POST /api/recipe-drafts` sends the text and the line's catalog to a language
   model, which returns a structured draft: name, description, weight, time, batch, and ingredients matched to
   catalog ids with the dictated amounts. The server keeps only ids from that catalog.
3. **Review → apply on the client.** The user sees the draft, then `RecipeDraftApplier` (Domain) merges it through
   the selection rules; ml ↔ drops are converted. Dictation adds to the recipe, so it can be done in parts.

The language model is reached through `ILlmClient` (`src/SoapAndSoul.Api/Llm`), so providers can be swapped.
Configured with the `Llm` section:

| Setting | Default | |
|---|---|---|
| `Llm:Provider` | `None` | `Anthropic` enables voice drafts; with `None` the microphone button is hidden. |
| `Llm:Model` | `claude-opus-5-5` | |
| `Llm:Effort` | `Low` | `Low`…`Max`; parsing a dictation is short extraction work. |
| `Llm:ApiKey` | — | Falls back to `ANTHROPIC_API_KEY` (or, in development, an `ant auth login` profile). Outside development, no key turns voice drafts off. |

For local development:

```bash
dotnet user-secrets set "Llm:Provider" "Anthropic" --project src/SoapAndSoul.Api
dotnet user-secrets set "Llm:ApiKey" "<key>" --project src/SoapAndSoul.Api
```

Requests opt into server-side refusal fallbacks (`fallbacks: "default"`), so a policy decline is retried on
Anthropic's recommended fallback model instead of failing.

## Deployment

Production runs on Azure App Service with Azure SQL (the free offer) and Blob Storage, reached through a managed identity.
On every push to `main`, GitHub Actions (`.github/workflows/deploy.yml`) builds and tests the app, deploys the
infrastructure from `infra/main.bicep`, then deploys the app onto it.
The Anthropic and MCP keys live in Key Vault and are set with `infra/set-secrets.sh`.
One-time Azure and GitHub setup: [docs/deploy/azure.md](docs/deploy/azure.md).

## Structure

| Project | Purpose |
|---|---|
| `SoapAndSoul.Domain` | Shared by client and server: categories, selection rules, costing, validation, search, formatting. No EF or UI. |
| `SoapAndSoul.Data` | EF Core: entities, `SoapAndSoulDbContext`, SQLite migrations, supplier catalog, sample data. |
| `SoapAndSoul.Data.SqlServer` | SQL Server (Azure SQL) setup and migrations, used in production. |
| `SoapAndSoul.Api` | ASP.NET Core Minimal API (`/api/...`) and MCP server (`/mcp`); also serves the client. Photos go to local disk or Azure Blob Storage (`IImageStorage`); language models behind `ILlmClient`. |
| `SoapAndSoul.Client` | Blazor WebAssembly PWA implementing the design in `docs/design/soap-and-soul-mobile-v2.html`. |
| `tests/*` | xUnit: rules and calculations (Domain), API integration tests on an in-memory SQLite database (Api), Playwright browser tests of the main flows (UI; `HEADED=1` shows the browser). |

Design reference: `docs/design/` (v2 prototype, Nocturne design system guide, design discussion).

## Domain rules

- **Unit cost**: purchase price ÷ quantity. Drops are bought in ml: 1 ml = 20 drops.
  A **mold** is reusable: its price is additionally divided by "uses per mold" (100 by default).
- **Mold / bottle** — one per recipe; its capacity becomes the recipe weight and the amount of base.
- **Base** — one per recipe; when selected, it takes the mold capacity (or the default portion if there is no mold).
- **Essential oils and fragrances** cannot be combined in soap: selecting one removes the other.
- New ingredients are added with their default portion.
- Required slots: soap — Mold, Base, Color, Extract, Packaging; perfume — Bottle, Base, Fragrance.

## Architecture decisions

- **A recipe is saved as a whole document** (`PUT /api/recipes/{id}`) with its full composition.
- **Auto-save**: ~1 s after the last change, and immediately when leaving the screen or backgrounding the app.
- **IDs are Guid v7, generated by the client.** Every record has a `Version` (optimistic concurrency; a conflict
  returns `409` with the current copy) and supports soft delete. This prepares the ground for offline mode.
- **Client data access goes through interfaces** `IRecipeStore`, `IIngredientStore`, `IImageStore`
  (currently `HttpDataStore`).
- The client runs with `InvariantGlobalization` (smaller download): numbers are formatted in `Domain/Text/Formatting.cs`,
  sorting uses `UkrainianComparer`.
- Photos are resized in the browser to 1280 px before upload; the server checks the file signature (JPEG/PNG/WebP).

## Next steps

- **Authentication** (multiple users): `[Authorize]` on the `/api` group, Entra ID or ASP.NET Identity; the same for `/mcp`.
- **Offline**: a second `IRecipeStore`/`IIngredientStore` implementation on IndexedDB + a change queue and sync by `Version`.
- **Tablet**: two-column layout (recipe + selection panel).
- Cleanup of photos that are no longer referenced.