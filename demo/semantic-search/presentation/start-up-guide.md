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
| VS Code launched with local FHIR MCP credentials in its parent environment | complete Step 1 before starting the demo agents |

---

## Step 1 — Launch VS Code with local MCP credentials

The FHIR MCP process inherits credentials from the VS Code process. If VS Code
was not launched from a terminal containing these variables, close all VS Code
windows first. Setting them later in an integrated terminal does not update the
already-running VS Code parent process.

These are local development credentials from `testauthenvironment.json`, not
Azure credentials. Keep production credentials out of source files and
`.vscode/mcp.json`.

```powershell
Set-Location '<path-to-fhir-server>'
$env:FHIR_MCP_CLIENT_ID = 'globalAdminServicePrincipal'
$env:FHIR_MCP_CLIENT_SECRET = 'globalAdminServicePrincipal'
code .
```

Open a new integrated PowerShell terminal in the launched workspace and continue
with Step 2.

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

## Step 3 — Build the FHIR server and MCP (once per code change)

```powershell
dotnet build .\src\Microsoft.Health.Fhir.R4.Web\Microsoft.Health.Fhir.R4.Web.csproj -c Debug -p:DotNetSdkPackageVersion=9.0.18
dotnet build .\tools\FhirMcp\FhirMcp.csproj -c Debug -p:DotNetSdkPackageVersion=9.0.18
```

The checked-in `.vscode/mcp.json` launches the MCP assembly from
`tools/FhirMcp/bin/Debug/net9.0`. Stop the `fhir` MCP server before rebuilding if
the DLL is locked.

## Step 4 — Sign in to Azure (for the embedding endpoint)

The server generates embeddings by calling an Azure OpenAI / Foundry deployment.
`AzureFoundryEmbeddingClient` authenticates with `DefaultAzureCredential` (your
developer sign-in locally, managed identity in production) — **no API key is used
or stored**. Sign in before starting the server so the credential is available
when indexing runs during `02`.

This Azure identity is separate from the local OAuth client credentials in Step
1. Azure authenticates the FHIR server to the embedding endpoint; the Step 1
credentials authenticate the MCP client to the local FHIR server.

```powershell
az login
```

If you belong to more than one tenant or subscription, select the one that owns
the Foundry resource:

```powershell
az account set --subscription '<subscription-id-or-name>'
$env:FHIR_DEMO_EMBEDDING_ENDPOINT = 'https://<your-foundry-resource>.cognitiveservices.azure.com'
```

Your signed-in identity needs the **Cognitive Services OpenAI User** role on the
configured Foundry resource. Confirm a token can be issued for the embedding
audience:

```powershell
az account get-access-token --resource https://cognitiveservices.azure.com | Out-Null; if ($?) { 'Azure sign-in OK' } else { 'STOP: run az login' }
```

## Step 5 — Set environment and start the server

Paste this single line. It guards against a missing password, forces the SQL
data store, keeps the issuer clean (no trailing-slash Authority override), uses
`--no-launch-profile` so `launchSettings.json` cannot force Cosmos, and targets
`net9.0`.

```powershell
if ([string]::IsNullOrWhiteSpace($env:FHIR_DEMO_SQL_PASSWORD)) { Write-Host 'STOP: run Step 2 first.' -ForegroundColor Red } elseif ([string]::IsNullOrWhiteSpace($env:FHIR_DEMO_EMBEDDING_ENDPOINT)) { Write-Host 'STOP: set the embedding endpoint in Step 4 first.' -ForegroundColor Red } else { Remove-Item Env:FhirServer__Security__Authentication__Authority -ErrorAction SilentlyContinue; $env:DataStore='SqlServer'; $env:SqlServer__ConnectionString="Server=127.0.0.1,14333;Initial Catalog=FHIR_R4;User ID=sa;Password=$env:FHIR_DEMO_SQL_PASSWORD;TrustServerCertificate=true"; $env:SqlServer__Initialize='true'; $env:SqlServer__AllowDatabaseCreation='false'; $env:SqlServer__SchemaOptions__AutomaticUpdatesEnabled='true'; $env:TestAuthEnvironment__FilePath="$PWD\testauthenvironment.json"; $env:ASPNETCORE_ENVIRONMENT='Development'; $env:ASPNETCORE_URLS='https://localhost:44348'; $env:FhirServer__CoreFeatures__VectorSearch__Enabled='true'; $env:FhirServer__CoreFeatures__VectorSearch__Embedding__Endpoint=$env:FHIR_DEMO_EMBEDDING_ENDPOINT; $env:FhirServer__CoreFeatures__VectorSearch__Embedding__DeploymentName='text-embedding-3-small'; $env:FhirServer__CoreFeatures__VectorSearch__Embedding__ModelName='text-embedding-3-small'; $env:FhirServer__CoreFeatures__VectorSearch__Embedding__ModelVersion='1'; $env:FhirServer__CoreFeatures__VectorSearch__Embedding__Dimensions='1536'; dotnet run --project .\src\Microsoft.Health.Fhir.R4.Web\Microsoft.Health.Fhir.R4.Web.csproj --framework net9.0 -c Debug --no-build --no-launch-profile -p:DotNetSdkPackageVersion=9.0.18 }
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

### Verify the server is listening (before using MCP)

Leave the server terminal running and open a **second** terminal to confirm the
port is actually bound. Every FHIR MCP tool is a thin proxy to this endpoint, so
if nothing is listening on `44348`, all MCP tool calls fail.

```powershell
Test-NetConnection 127.0.0.1 -Port 44348 -InformationLevel Quiet
```

`True` means the server is up. `False` means the run in Step 5 exited (exit code
1) — check that terminal for errors and re-run Step 5. The most common cause is
`$env:FHIR_DEMO_SQL_PASSWORD` not being set in the terminal that ran Step 5, which
produces an empty SQL password and a failed start.

## Step 6 — Prepare and validate the demo data (VS Code REST Client)

Each request file has a `# @name bearer` request. Send it first so a fresh token
is captured; restarting the server invalidates old tokens.

For the clinical MCP demo, use this minimal preparation path:

1. `00-preflight.http`: send `bearer`, then confirm `GET /metadata` returns a 200
	 CapabilityStatement.
2. `01-verify-search-parameters.http`: confirm the three built-in vector
	 SearchParameters are enabled.
3. `02-ingest-and-index.http`: run all 40 PUTs for a fresh database or when a
	 canonical fixture is missing. A normal rerun is idempotent when vectors are
	 already present.

Use `00-reset-vector-resources.http` only to recover resources that were created
without vectors or to force deliberate fresh indexing. It hard-deletes all 20
vector-bearing fixture owners and is not part of a normal rehearsal.

The remaining requests are optional implementation proofs and transparent
backups for the agent-generated queries:

- `03-standard-search.http`: deterministic and hybrid FHIR search.
- `04-semantic-search.http`: patient-wide meaning-based retrieval.
- `05-long-document-search.http`: long text and page-specific PDF retrieval.
- `07-radiology-search.http`: imaging chronology, varied report wording, and
	patient-isolation checks.
- `01-manage-custom-search-parameter.http`: posted vector SearchParameter
	lifecycle.
- `06-vector-reindex-proof.http`: existing-resource vector backfill through
	asynchronous system `$reindex`.

## Step 7: Connect and verify the FHIR MCP server

The MCP implementation and its environment-variable reference are documented in
`tools/FhirMcp/README.md`. The startup sequence for this demo is:

1. Run **MCP: List Servers** from the VS Code Command Palette.
2. Select `fhir`, then start or restart it and accept the workspace trust prompt.
3. Open **Show Output** and confirm that initialization completes without an
	authentication or startup error.
4. Confirm that these four tools are available:
	`patientSemanticSearch`, `searchFhirResources`, `readFhirResource`, and
	`discoverVectorSearchParameters`.
5. Verify the path end-to-end: with the tools loaded, a single
	`discoverVectorSearchParameters` call must succeed. If it errors, the MCP
	server itself is fine but the FHIR backend is unreachable — return to Step 5's
	verification (`Test-NetConnection 127.0.0.1 -Port 44348` must be `True`).

**If MCP "is not working," diagnose in this order:**

1. Is the server listening? `Test-NetConnection 127.0.0.1 -Port 44348` → must be
	`True`. If `False`, the backend is down; fix Step 2 (SQL password) and re-run
	Step 5.
2. Is the `fhir` server present in **MCP: List Servers**? If not, open the repo
	root as the workspace and reload the window.
3. Are the four tools listed and does `discoverVectorSearchParameters` succeed? A
	tool error with the port listening points to authentication — fully close VS
	Code and repeat Step 1.

The MCP registration contains only local endpoint configuration. Credentials are
inherited from the VS Code environment established in Step 1. Every FHIR call
writes sanitized captures under `%TEMP%\fhir-mcp-captures` by default. Captures
omit authorization values but can contain clinical data, so do not commit them.

## Step 8: Run the physician context demo agent

In VS Code Chat, select **Physician Context Demo** from the agent picker. Use it only
for the presentation, then switch back to the coding agent for development work.

In a fresh chat, establish the patient context first. This simulates a host such as
Dragon Copilot supplying the active patient before the clinical question:

```text
Session context: the active patient is Patient/8f789d0b-3145-4cf2-8504-13159edaa747. Keep all retrieval scoped to this patient until I explicitly change or clear the session. Confirm the active patient without searching.
```

After the agent confirms the context, ask the focused question naturally:

```text
Has the record documented previous dizziness, fainting, or near-fainting episodes?
```

The agent performs live, read-only hybrid retrieval and returns one grounded
paragraph for a focused question. It can also produce a bounded cross-specialist
summary when asked. Every clinical statement has a parenthetical, clickable FHIR
resource reference. A compact details section shows the structured patient scope,
semantic query, match count, and exact passages used for each paraphrase.

The clickable citations identify the live owner and evidence-source resources.
Opening them directly can still require FHIR authentication. The raw response to
the patient operation is an authenticated POST response, so it is not a persistent
browser link. To inspect the complete raw Bundle, send the matching request in
`demo/semantic-search/requests/04-semantic-search.http`; REST Client displays the
response in its response pane.

## Step 9: Run the radiology context demo agent

Start a fresh chat, select **Radiology Context Demo**, and send the same session
context message from Step 8. The agent searches radiology report text and FHIR
metadata; it does not inspect or interpret source images.

After the agent confirms the active patient, use these three flows:

```text
Was the lung nodule follow-up completed, and did the finding change?
```

```text
What follow-up did prior chest imaging recommend?
```

```text
Did the right upper lobe finding grow on later imaging?
```

The agent first establishes the comparable radiology timeline with structured
FHIR search, then uses semantic retrieval for report wording such as pulmonary
nodule, focal opacity, and focal density. The expected report-based answer traces
the baseline recommendation, the six-month study, and the eighteen-month study.
It must not include either control-patient report. Search details expose the live
requests, exact report passages, evidence provenance, and sanitized captures.

Use `demo/semantic-search/requests/07-radiology-search.http` to inspect equivalent
raw FHIR responses independently of the agent.

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
| `fhir` is missing from **MCP: List Servers** | The workspace MCP registration was not loaded. | Open the repo root as the VS Code workspace and reload the window. |
| MCP starts but its four tools are unavailable | The MCP assembly is missing, stale, or failed during initialization. | Stop `fhir`, run the MCP build from Step 3, restart it, and inspect **Show Output**. |
| MCP tools fail with connection or authentication errors | The FHIR server is not listening on `44348`, or VS Code did not inherit the Step 1 credentials. | Confirm Step 5 is still running. If authentication fails, fully close VS Code and repeat Step 1. |
| The demo agent says the FHIR tools are unavailable after an MCP restart | The chat session was created before the tools were available. | Confirm the four tools in **MCP: List Servers**, then start a fresh agent chat. |
| MCP rebuild fails because `Microsoft.Health.Fhir.Mcp.dll` is locked | The stdio MCP process is still running. | Stop `fhir` through **MCP: List Servers**, rebuild, and restart it. |
