# Start-Up Guide (PowerShell)

How to bring up the SQL-backed R4 FHIR server for the semantic-search demo on a
local Windows machine. Run every step in **PowerShell 7+ (`pwsh`)**.

---

## Prerequisites (verify once)

| Requirement | Check |
|---|---|
| SQL Server 2025 container running on `127.0.0.1,14333` (database `FHIR_R4`) | `docker ps --filter 'publish=14333'` |
| Dev HTTPS certificate trusted (so the server can read its own OIDC metadata) | `dotnet dev-certs https --trust` |
| .NET SDK build override (repo default 9.0.15 is blocked by a NuGet audit CVE) | pass `-p:DotNetSdkPackageVersion=9.0.18` on build/run |
| Azure CLI signed in with access to the Foundry embedding resource | `az account get-access-token --resource https://cognitiveservices.azure.com` |

---

## Step 1 — Open a fresh terminal and go to the repo

```powershell
Set-Location 'C:\Users\t-annag\fhir-server'
```

## Step 2 — Provide the SQL password

Pick **one** option. The password is a secret — never paste it into chat.

**Option A — set it yourself (type the value directly):**

```powershell
$env:FHIR_DEMO_SQL_PASSWORD = '<your-sa-password>'
```

**Option B — derive it from the running container (never printed):**

```powershell
$env:FHIR_DEMO_SQL_PASSWORD = (docker inspect fhir-semantic-sql2025 --format '{{range .Config.Env}}{{println .}}{{end}}' | Select-String '^MSSQL_SA_PASSWORD=' | ForEach-Object { ($_ -split '=',2)[1] } | Select-Object -First 1).Trim()
```

## Step 3 — Build (once per code change)

```powershell
dotnet build .\src\Microsoft.Health.Fhir.R4.Web\Microsoft.Health.Fhir.R4.Web.csproj -c Debug -p:DotNetSdkPackageVersion=9.0.18
```

## Step 4 — Sign in to Azure (for the embedding endpoint)

The server generates embeddings by calling an Azure OpenAI / Foundry deployment.
`AzureFoundryEmbeddingClient` authenticates with `DefaultAzureCredential` (your
developer sign-in locally, managed identity in production) — **no API key is used
or stored**. Sign in before starting the server so the credential is available
when indexing runs during `02`.

```powershell
az login
```

If you belong to more than one tenant or subscription, select the one that owns
the Foundry resource:

```powershell
az account set --subscription '<subscription-id-or-name>'
```

Your signed-in identity needs the **Cognitive Services OpenAI User** role on the
Foundry resource (`https://anna-foundry.cognitiveservices.azure.com`). Confirm a
token can be issued for the embedding audience:

```powershell
az account get-access-token --resource https://cognitiveservices.azure.com | Out-Null; if ($?) { 'Azure sign-in OK' } else { 'STOP: run az login' }
```

## Step 5 — Set environment and start the server

Paste this single line. It guards against a missing password, forces the SQL
data store, keeps the issuer clean (no trailing-slash Authority override), uses
`--no-launch-profile` so `launchSettings.json` cannot force Cosmos, and targets
`net9.0`.

```powershell
if ([string]::IsNullOrWhiteSpace($env:FHIR_DEMO_SQL_PASSWORD)) { Write-Host 'STOP: run Step 2 first.' -ForegroundColor Red } else { Set-Location 'C:\Users\t-annag\fhir-server'; Remove-Item Env:FhirServer__Security__Authentication__Authority -ErrorAction SilentlyContinue; $env:DataStore='SqlServer'; $env:SqlServer__ConnectionString="Server=127.0.0.1,14333;Initial Catalog=FHIR_R4;User ID=sa;Password=$env:FHIR_DEMO_SQL_PASSWORD;TrustServerCertificate=true"; $env:SqlServer__Initialize='true'; $env:SqlServer__AllowDatabaseCreation='false'; $env:SqlServer__SchemaOptions__AutomaticUpdatesEnabled='true'; $env:TestAuthEnvironment__FilePath="$PWD\testauthenvironment.json"; $env:ASPNETCORE_ENVIRONMENT='Development'; $env:ASPNETCORE_URLS='https://localhost:44348'; $env:FhirServer__CoreFeatures__VectorSearch__Enabled='true'; $env:FhirServer__CoreFeatures__VectorSearch__Embedding__Endpoint='https://anna-foundry.cognitiveservices.azure.com'; $env:FhirServer__CoreFeatures__VectorSearch__Embedding__DeploymentName='text-embedding-3-small'; $env:FhirServer__CoreFeatures__VectorSearch__Embedding__ModelName='text-embedding-3-small'; $env:FhirServer__CoreFeatures__VectorSearch__Embedding__ModelVersion='1'; $env:FhirServer__CoreFeatures__VectorSearch__Embedding__Dimensions='1536'; dotnet run --project .\src\Microsoft.Health.Fhir.R4.Web\Microsoft.Health.Fhir.R4.Web.csproj --framework net9.0 -c Debug --no-build --no-launch-profile -p:DotNetSdkPackageVersion=9.0.18 }
```

Leave this terminal running. A healthy start shows:

```
Schema version is 118
Initializing 127.0.0.1,14333 FHIR_R4 to version 118
Initialized 1380 search parameters.
Now listening on: https://localhost:44348
Application started. Press Ctrl+C to shut down.
```

There should be **no** `localhost:8081` / Cosmos messages.

## Step 6 — Run the demo requests (VS Code REST Client)

Run these in order. Each file has a `# @name bearer` request — send it first so a
fresh token is captured (restarting the server invalidates old tokens).

1. `00-preflight.http` — `bearer`, then `GET /metadata` (200 CapabilityStatement).
2. `01-verify-search-parameters.http` — three `$status` checks for the built-in params.
3. `02-ingest-and-index.http` — ingest + index.
4. `03-standard-search.http` — deterministic FHIR search.
5. `04-semantic-search.http` — meaning-based retrieval.
6. `05-long-document-search.http` — long text and page-specific PDF retrieval.
7. `06-vector-reindex-proof.http` — existing-resource vector backfill through
	asynchronous system `$reindex`.

---

## Stop / restart the server

```powershell
$procs = (Get-NetTCPConnection -LocalPort 44348 -State Listen -ErrorAction SilentlyContinue).OwningProcess | Select-Object -Unique; foreach ($id in $procs) { Stop-Process -Id $id -Force }
```

Then re-run Step 5.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `500 ... No connection ... (localhost:8081)` | Server started against Cosmos. The `Default` launch profile sets `DataStore=CosmosDb` and overrides the shell var. | Ensure Step 5 includes `--no-launch-profile`; restart. |
| `401 ... The issuer 'https://localhost:44348/' is invalid` | Stale process without the issuer fix, or an old token. | Restart via Step 5 and send the `bearer` request again for a fresh token. |
| "The connection was rejected" in REST Client | Nothing is listening on 44348 (the run exited). | Check the server terminal for errors; restart via Step 5. |
| `dotnet run` errors: "Specify which framework" | Project multi-targets. | Include `--framework net9.0` (already in the Step 5 command). |
| NuGet audit CVE blocks build | Repo default SDK package 9.0.15. | Keep `-p:DotNetSdkPackageVersion=9.0.18`. |
| Empty SQL password in connection string | Step 5 ran in a terminal where `$env:FHIR_DEMO_SQL_PASSWORD` was not set. | Run Step 2 in the same terminal first. |
| `401`/`403` from the embedding endpoint during `02` ingest | Not signed in to Azure, wrong tenant/subscription, or missing role. | Complete Step 4 (`az login`), select the subscription that owns the Foundry resource, and ensure the **Cognitive Services OpenAI User** role. |
| `SELECT COUNT(*) FROM dbo.VectorSearchParam` is `0` after `02` | Embedding endpoint never invoked: `VectorSearch` disabled, not signed in, or no text to embed. | Confirm Step 5 sets `VectorSearch__Enabled=true`; complete Step 4; watch the server console for `Vector indexing invoking embedding endpoint for N passage(s)`. |
