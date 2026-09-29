# Deploying to Azure

Production runs on Azure App Service with Azure SQL and Blob Storage. Everything is described in
[`infra/main.bicep`](../../infra/main.bicep) and deployed by
[`.github/workflows/deploy.yml`](../../.github/workflows/deploy.yml):

- every push and pull request: restore → build → test → validate Bicep → publish;
- push to `main` (or a manual run on `main`), in the `production` environment:
  1. deploy `infra/main.bicep` to the resource group (idempotent; unchanged resources are left alone);
  2. deploy the app to the Web App from the template outputs;
  3. poll `/healthz` until it answers.

GitHub signs in to Azure with OpenID Connect. The app signs in to SQL, Blob Storage and Key Vault with a managed
identity. No passwords, connection secrets or storage keys exist anywhere. The only secrets are third-party API keys
(Anthropic, the MCP key); they live in Key Vault and are set by hand, never by the template or the pipeline.

## What gets deployed

| Resource | Name | Notes |
|---|---|---|
| User-assigned managed identity | `id-soapandsoul` | The app's identity for SQL and Blob Storage |
| Azure SQL server + database | `sql-soapandsoul-<hash>` / `soapandsoul` | Entra-only auth, Basic tier (5 DTU, 2 GB) |
| Storage account + `images` container | `stsoapandsoul<hash>` | No public access, shared keys disabled, 7-day soft delete |
| App Service plan | `asp-soapandsoul` | Linux B1; skipped when an existing plan is reused |
| Web App | `app-soapandsoul-<hash>` | .NET 10, HTTPS only, Always On, health check `/healthz` |
| Key Vault | `kvsoapandsoul<hash>` | RBAC only; the app identity has *Key Vault Secrets User* |

`<hash>` is derived from the resource group id, so names are stable across deployments. The names and SKUs
are parameters in `main.bicep`; set them in [`infra/main.bicepparam`](../../infra/main.bicepparam).

### How the app uses them

The template writes these app settings; nothing is configured by hand:

| Setting | Value |
|---|---|
| `Database__Provider` | `SqlServer` |
| `ConnectionStrings__Default` | `Server=tcp:<server>.database.windows.net;…;Authentication=Active Directory Managed Identity;User Id=<identity client id>` |
| `Images__Provider` | `AzureBlob` |
| `Images__BlobServiceUri` / `Images__Container` | the storage account's blob endpoint / `images` |
| `Images__ManagedIdentityClientId` | the identity's client id |
| `KeyVault__Uri` / `KeyVault__ManagedIdentityClientId` | the vault's URI / the identity's client id |
| `Llm__Provider` | `Anthropic` |

- **SQL**: the app applies the SQL Server migrations (`src/SoapAndSoul.Data.SqlServer/Migrations`) at startup.
  Connections retry on transient Azure SQL errors.
- **Photos**: uploaded to the private `images` container. The app streams them to the browser at
  `/images/{name}`, so photo URLs stay the same as in development and the storage account stays private.
  The identity has *Storage Blob Data Contributor* on the `images` container only.
- **Secrets**: at startup the app loads the vault's secrets into configuration (`--` stands for `:`):

  | Secret | Setting | Without it |
  |---|---|---|
  | `Llm--ApiKey` | `Llm:ApiKey` | voice drafts are off (the microphone button is hidden) |
  | `Mcp--ApiKey` | `Mcp:ApiKey` | `/mcp` is off (404); with it, clients send `Authorization: Bearer <key>` |

  Secrets are read once, so the app is restarted after a change (`infra/set-secrets.sh` does it).

### Security trade-offs

- **The app identity is the SQL server's Entra admin.** Azure SQL has no declarative way to create a database
  user for a managed identity; that needs T-SQL run by an admin, plus Microsoft Graph permissions for the
  server. Making the app identity the admin keeps the whole setup in Bicep and lets the app run migrations.
  The server hosts only this database. To move to least privilege later, make an Entra group the admin, create
  a contained user for the identity (`CREATE USER [id-soapandsoul] FROM EXTERNAL PROVIDER`) with
  `db_datareader`/`db_datawriter`/`db_ddladmin`, and remove the `administrators` block.
- **SQL firewall allows Azure services** (`0.0.0.0` rule), because App Service outbound IPs are only known after
  deployment. Every connection still needs an Entra token for the admin identity. Private endpoints would
  close this completely but need a VNet and a higher App Service tier.

## One-time setup

Run with the [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) after `az login`.
You need Owner (or User Access Administrator) on the resource group to grant the roles below.
The commands are bash. In Git Bash on Windows, run `export MSYS_NO_PATHCONV=1` first, otherwise arguments
such as `/subscriptions/...` are rewritten into Windows paths.

```bash
RG=<resource-group>            # the old site's resource group, or a new one
LOCATION=westeurope            # only used if the group is new
REPO=vovanreksi2/Soap-Soul

az group create -n $RG -l $LOCATION        # skip if it exists
RG_ID=$(az group show -n $RG --query id -o tsv)
```

### 1. Deployment identity for GitHub Actions

```bash
CLIENT_ID=$(az ad app create --display-name "soapandsoul-github-deploy" --query appId -o tsv)
az ad sp create --id $CLIENT_ID
az ad app federated-credential create --id $CLIENT_ID --parameters "{
  \"name\": \"github-production\",
  \"issuer\": \"https://token.actions.githubusercontent.com\",
  \"subject\": \"repo:$REPO:environment:production\",
  \"audiences\": [\"api://AzureADTokenExchange\"]
}"

# GitHub may send the subject in its ID-based form instead
# (repo:<owner>@<owner-id>/<repo>@<repo-id>:environment:production), so trust that one too.
# Needs the GitHub CLI (gh auth login).
OWNER_ID=$(gh api repos/$REPO --jq .owner.id)
REPO_ID=$(gh api repos/$REPO --jq .id)
az ad app federated-credential create --id $CLIENT_ID --parameters "{
  \"name\": \"github-production-ids\",
  \"issuer\": \"https://token.actions.githubusercontent.com\",
  \"subject\": \"repo:${REPO%%/*}@$OWNER_ID/${REPO#*/}@$REPO_ID:environment:production\",
  \"audiences\": [\"api://AzureADTokenExchange\"]
}"

# Create and update resources in the group.
az role assignment create --assignee $CLIENT_ID --role Contributor --scope $RG_ID

# Assign roles, but only the two the template grants the app identity:
# "Storage Blob Data Contributor" and "Key Vault Secrets User".
BLOB_ROLE=ba92f5b4-2d11-453d-a403-e96b0029c9fe
KV_ROLE=4633458b-17de-408a-b874-0445c86b69e6
az role assignment create --assignee $CLIENT_ID --role "Role Based Access Control Administrator" --scope $RG_ID \
  --condition-version 2.0 --condition "((!(ActionMatches{'Microsoft.Authorization/roleAssignments/write'})) OR (@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$BLOB_ROLE, $KV_ROLE})) AND ((!(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'})) OR (@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$BLOB_ROLE, $KV_ROLE}))"

echo "AZURE_CLIENT_ID=$CLIENT_ID"
echo "AZURE_TENANT_ID=$(az account show --query tenantId -o tsv)"
echo "AZURE_SUBSCRIPTION_ID=$(az account show --query id -o tsv)"
echo "AZURE_RESOURCE_GROUP=$RG"
```

The federated credential only works for jobs in the `production` environment of this repository.

If the login step still fails with `AADSTS700213: No matching federated identity record found for presented assertion
subject '...'` (for example, after the repository was renamed or transferred), add another federated credential
whose `subject` is exactly the value quoted in the error.

If the deployment identity already has this role with the older, Blob-only condition, delete that assignment
(`az role assignment delete --assignee $CLIENT_ID --role "Role Based Access Control Administrator" --scope $RG_ID`)
and create it again with the command above.

### 2. GitHub repository

1. **Settings → Environments → New environment** `production`. Optionally add required reviewers and limit
   deployment branches to `main`.
2. **Settings → Secrets and variables → Actions → Variables**: add the four values printed above.
   They are identifiers, not secrets.
3. Push to `main` or run **Actions → Build and deploy → Run workflow**. The first run creates everything
   (SQL takes a few minutes); later runs only update what changed.

### 3. Secrets

After the first deployment has created the vault, put the keys into it:

```bash
bash infra/set-secrets.sh $RG     # asks for the Anthropic API key (or reads ANTHROPIC_API_KEY), generates the MCP key
```

The script grants you *Key Vault Secrets Officer* on the vault, writes `Llm--ApiKey` and a random `Mcp--ApiKey`
(existing ones are kept), and restarts the app. Later: `--anthropic` replaces the Anthropic key, `--rotate-mcp`
issues a new MCP key. Read the MCP key for a client with
`az keyvault secret show --vault-name <vault> -n Mcp--ApiKey --query value -o tsv`, then:

```bash
claude mcp add --transport http soap-and-soul https://<app>.azurewebsites.net/mcp --header "Authorization: Bearer <key>"
```

### 4. Reusing the old site

- **Resource group**: use it as `AZURE_RESOURCE_GROUP`. The template only adds its own resources and leaves
  the others alone (deployments are incremental).
- **App Service plan**: set `appServicePlanId` in `infra/main.bicepparam` to the old plan's resource id
  (`az appservice plan show -g $RG -n <plan> --query id -o tsv`). The plan must be **Linux** and in the same
  region. A Windows plan cannot host this template's Linux app; leave the parameter empty to get a new plan.
- **Web App name**: set `webAppName` if you want a specific `<name>.azurewebsites.net`. To reuse the old Web App
  itself, it must be a Linux app; its settings are replaced by the template's.
- **Custom domain**: point a `CNAME` at the new Web App (plus the `asuid` TXT record App Service asks for), then:

  ```bash
  APP=$(az webapp list -g $RG --query "[?starts_with(name, 'app-soapandsoul')].name | [0]" -o tsv)
  DOMAIN=<app.example.com>
  az webapp config hostname add -g $RG --webapp-name $APP --hostname $DOMAIN
  az webapp config ssl create -g $RG -n $APP --hostname $DOMAIN          # free managed certificate
  az webapp config ssl bind -g $RG -n $APP --ssl-type SNI \
    --certificate-thumbprint $(az webapp config ssl list -g $RG \
      --query "[?subjectName=='$DOMAIN'].thumbprint | [0]" -o tsv)
  ```

## Operations

- **Logs**: `az webapp log tail -g $RG -n $APP` (enable with `az webapp log config -g $RG -n $APP --docker-container-logging filesystem`).
- **Backups**: Azure SQL keeps point-in-time restore for 7 days (Basic tier); deleted blobs are kept for 7 days.
- **Rollback**: re-run the deploy job of an earlier successful workflow run. Migrations are not rolled back.
- **Querying the database yourself**: only the app identity is admin. Temporarily make yourself admin and allow
  your IP; the next deployment restores the app identity:

  ```bash
  SQL=$(az sql server list -g $RG --query "[0].name" -o tsv)
  az sql server ad-admin create -g $RG -s $SQL --display-name "$(az ad signed-in-user show --query userPrincipalName -o tsv)" \
    --object-id $(az ad signed-in-user show --query id -o tsv)
  az sql server firewall-rule create -g $RG -s $SQL -n me --start-ip-address <your-ip> --end-ip-address <your-ip>
  ```

  Remove the `me` firewall rule when you are done.

## Changing the data model

Each provider has its own migrations. After changing the model, add a migration to both:

```bash
dotnet ef migrations add <Name> --project src/SoapAndSoul.Data              # SQLite (development)
dotnet ef migrations add <Name> --project src/SoapAndSoul.Data.SqlServer    # Azure SQL (production)
```

`MigrationTests` fails if either set is behind the model.
