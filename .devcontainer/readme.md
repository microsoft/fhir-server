# Develop FHIR Server in a container

Use the SQL Server 2025 devcontainer to build, test, and run FHIR Server without a host .NET SDK.
Select the Cosmos DB emulator only when you need that provider.
Use VS Code or the Dev Containers CLI. CLI startup prepares the database and debugger environment, but does not start FHIR.

## Prepare your machine

- Install Docker with Compose v2 or later. On Windows, use Docker Desktop in Linux container mode.
- Use a regular clone for container-side Git. Linux cannot follow a Windows worktree's external `.git` path.
  On Windows, choose a short clone path to avoid Git path-length limits.
  Initialization bind-mounts the local clone. Remote Docker daemons are not covered by this workflow.
- Use an x64 host with at least two CPUs and enough Docker memory for the database and builds.
  SQL Server requires at least 2 GB. The Cosmos emulator has a 3 GB limit.
  ARM hosts and the vNext emulator are not supported here.
- Allow access to MCR, GHCR, Ubuntu package repositories, `packages.microsoft.com`, .NET downloads,
  the npm registry, and the feeds in `nuget.config`.
  If your network intercepts TLS, follow your organization's approved container certificate setup.
  Keep TLS validation enabled.

For the CLI, install a supported [Node.js](https://nodejs.org/) release and the
[Dev Containers CLI](https://github.com/devcontainers/cli). Run these commands in a host terminal:

```text
npm install --global @devcontainers/cli
node --version
devcontainer --version
docker version
docker compose version
```

For the editor alternative, install [Visual Studio Code](https://code.visualstudio.com/) and its
[Dev Containers extension](https://marketplace.visualstudio.com/items?itemName=ms-vscode-remote.remote-containers).
Open the clone and run **Dev Containers: Reopen in Container**, selecting **FHIR SQL Server**.
Wait for **Running postStartCommand** to finish.
Use one lifecycle owner per clone. Do not run competing editor and CLI startup operations.
For multiple clones, use the explicit project setup below. An editor must inherit that same project environment.

The Docker-outside-of-Docker feature gives `vscode` access to the host Docker daemon.
Use this setup only with trusted repository code. It is not a security sandbox.
The local database workflow provisions no Azure resources.
Package restore still needs access to the configured public feeds.

## Save a provider-neutral project identity

Run all host commands from the clone root.
Before the first startup, choose a unique Compose project and save it locally.
Separate clone paths can have the same basename and select the same default project.
Do not rely on a folder name for isolation.

The following alternatives create a name once in this regular clone's local Git configuration.
Run the matching identity block again in every new host shell to restore `COMPOSE_PROJECT_NAME`.
These blocks do not start either provider.
The CLI reads the environment variable, not the custom Git setting.
Do not copy this setting to another clone or generate a new name on each restart.
If you already have a database project, verify its ownership and save its existing name instead of selecting new storage.

Host Bash:

```bash
set -e
if ! project="$(git config --local --get fhir.devcontainerProject)"; then
	project="fhir-$(node -p "require('node:crypto').randomUUID()")"
	git config --local fhir.devcontainerProject "$project"
fi
export COMPOSE_PROJECT_NAME="$project"
```

Host PowerShell:

```powershell
$ErrorActionPreference = 'Stop'
$project = git config --local --get fhir.devcontainerProject
if ($LASTEXITCODE -eq 1) {
	$project = "fhir-$([guid]::NewGuid().ToString())"
	git config --local fhir.devcontainerProject $project
	if ($LASTEXITCODE -ne 0) { throw 'Could not save the local project name.' }
} elseif ($LASTEXITCODE -ne 0) {
	throw 'Could not read the local project name.'
}
$env:COMPOSE_PROJECT_NAME = $project
```

## Start SQL

After restoring the project identity, start only your selected provider.
For SQL (the default), run the matching startup block below.
For Cosmos, skip SQL startup and [select Cosmos explicitly](#select-cosmos-explicitly).

Host Bash:

```bash
set -e
result="$(devcontainer up --workspace-folder . --config .devcontainer/devcontainer.json)"
container_id="$(printf '%s' "$result" | node -pe "JSON.parse(require('node:fs').readFileSync(0, 'utf8')).containerId")"
```

Host PowerShell:

```powershell
$ErrorActionPreference = 'Stop'
$result = devcontainer up --workspace-folder . --config .devcontainer/devcontainer.json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $result.outcome -ne 'success') { throw 'Devcontainer startup failed.' }
$containerId = $result.containerId
```

Keep the returned container ID for the ownership checks below.
Successful startup means Compose passed an authenticated SQL healthcheck and the post-start readiness check finished.
Check the user, SDKs, and Docker access from either host shell:

```text
devcontainer exec --workspace-folder . --config .devcontainer/devcontainer.json id
devcontainer exec --workspace-folder . --config .devcontainer/devcontainer.json dotnet --list-sdks
devcontainer exec --workspace-folder . --config .devcontainer/devcontainer.json dotnet --version
devcontainer exec --workspace-folder . --config .devcontainer/devcontainer.json docker version
devcontainer exec --workspace-folder . --config .devcontainer/devcontainer.json bash -lc 'cd /workspace/build/dotnet10-compat && dotnet --version'
```

The user is `vscode`. Both pinned SDKs, currently `11.0.100` and `10.0.404`, must be present.
The workspace root selects SDK 11. The compatibility directory selects SDK 10.
Targeting `net10.0` from the workspace root still uses SDK 11.

Initialization creates one random SQL password in `.devcontainer/.local/sql-password`.
Git and Docker build contexts exclude that directory.
Keep the file private and retain it with the project's `sql-data` Docker volume.
Do not print it, copy it into a tracked environment file, or paste it into startup commands or bug reports.
The SQL container reads the secret at runtime and runs SQL Server as `mssql`.
Compose configuration does not contain the password.

Starting, stopping, and rebuilding the developer container preserve the SQL volume.
Startup rejects a credential that does not match that volume instead of changing the database password.
Do not delete the credential while keeping the volume.

## Build and run focused tests

Open an in-container Bash terminal:

```text
devcontainer exec --workspace-folder . --config .devcontainer/devcontainer.json bash
```

Run the following commands in that terminal, not with a host SDK:

```bash
set -e
cd /workspace
dotnet restore src/Microsoft.Health.Fhir.Core.UnitTests/Microsoft.Health.Fhir.Core.UnitTests.csproj
dotnet build src/Microsoft.Health.Fhir.Core.UnitTests/Microsoft.Health.Fhir.Core.UnitTests.csproj --framework net11.0 --no-restore
dotnet build src/Microsoft.Health.Fhir.Core.UnitTests/Microsoft.Health.Fhir.Core.UnitTests.csproj --framework net10.0 --no-restore
dotnet test --project src/Microsoft.Health.Fhir.Core.UnitTests/Microsoft.Health.Fhir.Core.UnitTests.csproj --framework net11.0 --no-build --no-restore -- --filter-class Microsoft.Health.Fhir.Core.UnitTests.Models.PartialDateTimeTests
dotnet test --project src/Microsoft.Health.Fhir.Core.UnitTests/Microsoft.Health.Fhir.Core.UnitTests.csproj --framework net10.0 --no-build --no-restore -- --filter-class Microsoft.Health.Fhir.Core.UnitTests.Models.PartialDateTimeTests
```

Require a successful exit and a nonzero executed test count on each framework.
These commands select one existing unit-test class, not the whole suite.
The repository uses Microsoft.Testing.Platform. Do not substitute VSTest `--filter` syntax.
Integration and E2E tests need their own fixtures and configuration. This recipe does not run them.
If restore fails, record the feed and TLS error. An offline cache is not proof of public online restore or advisory checks.

Build the .NET 11 R4 application before the optional HTTP check or R4 debugger launch:

```bash
set -e
dotnet restore src/Microsoft.Health.Fhir.R4.Web/Microsoft.Health.Fhir.R4.Web.csproj
dotnet build src/Microsoft.Health.Fhir.R4.Web/Microsoft.Health.Fhir.R4.Web.csproj --framework net11.0 --no-restore
```

Do not build the same clone concurrently from Windows and Linux.
Use a separate checkout for host builds. Each concurrent agent needs its own regular clone,
saved unique project name, credential, SQL volume, and build outputs.
Within one clone, serialize lifecycle operations, builds, and database-mutating checks.

## Run an optional trusted R4 HTTP check

Both providers disable FHIR authentication for local development.
This is a synthetic local smoke check, not authorization testing.
Only one FHIR process can use port 44348 in a developer container at a time.

In the first in-container Bash terminal, load the generated environment as data and launch the same DLL as the debugger:

```bash
set -e
set +x
while IFS= read -r entry; do
	export "$entry"
done < "$HOME/.fhir-devcontainer/R4.env"
cd /workspace/src/Microsoft.Health.Fhir.R4.Web
printf '%s\n' "$$" > "$HOME/.fhir-devcontainer/R4.pid"
exec dotnet bin/Debug/net11.0/Microsoft.Health.Fhir.R4.Web.dll
```

Do not use `source`, `eval`, `export $(...)`, or `dotnet run`.
The debugger file contains raw `KEY=value` records, including a connection string with spaces and semicolons.
Do not print its contents or enable shell tracing.
SQL database creation and schema initialization happen when the app starts, not during CLI bootstrap.

Open a second in-container Bash terminal with the same explicit CLI configuration.
Export only the public development certificate and make a bounded, certificate-verified request:

```bash
set -e
dotnet dev-certs https --export-path "$HOME/.fhir-devcontainer/https.pem" --format PEM
timeout 150s curl --fail --silent --show-error --connect-timeout 3 --max-time 10 \
	--retry 30 --retry-connrefused --retry-delay 2 --retry-max-time 120 \
	--cacert "$HOME/.fhir-devcontainer/https.pem" \
	--header 'Accept: application/fhir+json' \
	https://localhost:44348/metadata --output "$HOME/.fhir-devcontainer/metadata.json"
grep -Eq '"resourceType"[[:space:]]*:[[:space:]]*"CapabilityStatement"' "$HOME/.fhir-devcontainer/metadata.json"
```

Require all three commands to succeed. The last checks the FHIR metadata response.
Do not add `--insecure` or change the system trust store for this probe.
Plain CLI startup does not establish VS Code port forwarding or make this URL reachable from the host.

Stop only the application you launched. In the second in-container Bash terminal, verify its PID, owner, directory, and command before sending SIGTERM:

```bash
set -e
pid="$(cat "$HOME/.fhir-devcontainer/R4.pid")"
[[ "$pid" =~ ^[0-9]+$ ]]
[[ "$(stat -c %u "/proc/$pid")" == "$(id -u)" ]]
[[ "$(readlink -f "/proc/$pid/cwd")" == /workspace/src/Microsoft.Health.Fhir.R4.Web ]]
mapfile -d '' -t argv < "/proc/$pid/cmdline"
[[ "${argv[0]##*/}" == dotnet && "${argv[1]}" == bin/Debug/net11.0/Microsoft.Health.Fhir.R4.Web.dll ]]
kill -TERM "$pid"
for attempt in {1..90}; do
	if ! kill -0 "$pid" 2>/dev/null; then
		rm "$HOME/.fhir-devcontainer/R4.pid"
		break
	fi
	sleep 1
done
if kill -0 "$pid" 2>/dev/null; then
	echo 'The owned FHIR process did not exit within 90 seconds. Inspect it before further action.' >&2
	exit 1
fi
```

Confirm the first terminal's attached command has exited.
Do not assume interrupting the host CLI killed the process in the container.
If the PID record is stale or ownership checks fail, inspect that exact process rather than killing by name.

## Verify the project and stop SQL

Exiting a CLI shell does not stop either Compose service.
Before provider switching or cleanup, use the returned container ID to check ownership.
Run the following from the same clone root and restore its saved project environment first if this is a new host shell.

Host Bash:

```bash
set -e
project="$(docker inspect --format '{{ index .Config.Labels "com.docker.compose.project" }}' "$container_id")"
[[ -n "$project" && "$project" == "$COMPOSE_PROJECT_NAME" ]]
workspace="$(docker inspect --format '{{ index .Config.Labels "devcontainer.local_folder" }}' "$container_id")"
node -e 'if (require("node:path").relative(process.argv[1], process.cwd())) { console.error("Container workspace does not match this clone."); process.exit(1); }' "$workspace"
[[ "$(docker inspect --format '{{ index .Config.Labels "com.docker.compose.service" }}' "$container_id")" == fhir-sql ]]
docker compose --project-name "$project" -f .devcontainer/docker-compose.sql.yml ps --all
docker compose --project-name "$project" -f .devcontainer/docker-compose.sql.yml stop fhir-sql sql
```

Host PowerShell:

```powershell
$labels = docker inspect --format '{{json .Config.Labels}}' $containerId | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or
	$labels.'com.docker.compose.project' -ne $env:COMPOSE_PROJECT_NAME -or
	$labels.'devcontainer.local_folder' -ne (Get-Location).ProviderPath -or
	$labels.'com.docker.compose.service' -ne 'fhir-sql') {
	throw 'Container ownership does not match this clone and SQL configuration.'
}
$project = $labels.'com.docker.compose.project'
docker compose --project-name $project -f .devcontainer/docker-compose.sql.yml ps --all
docker compose --project-name $project -f .devcontainer/docker-compose.sql.yml stop fhir-sql sql
if ($LASTEXITCODE -ne 0) { throw 'Project stop failed.' }
```

To restart SQL, restore the saved identity if this is a new host shell, then re-run the SQL startup block.
It must reuse the same project, credential, and volume.
Do not run `down --volumes`, delete credentials, prune Docker, or remove orphans as routine cleanup.
Separate projects are cooperative isolation, not protection from code with host Docker access.

## Select Cosmos explicitly

If SQL is running for this clone, stop its verified project first.
Keep the same clone's saved project name.
In a new host shell, restore the [provider-neutral identity](#save-a-provider-neutral-project-identity)
without starting SQL.
Select the nested configuration on every Cosmos `up` and `exec`.

Host Bash:

```bash
result="$(devcontainer up --workspace-folder . --config .devcontainer/cosmos/devcontainer.json)"
container_id="$(printf '%s' "$result" | node -pe "JSON.parse(require('node:fs').readFileSync(0, 'utf8')).containerId")"
```

Host PowerShell:

```powershell
$result = devcontainer up --workspace-folder . --config .devcontainer/cosmos/devcontainer.json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $result.outcome -ne 'success') { throw 'Cosmos startup failed.' }
$containerId = $result.containerId
```

Open the selected container:

```text
devcontainer exec --workspace-folder . --config .devcontainer/cosmos/devcontainer.json bash
```

In-container Bash:

```bash
curl --fail --silent --show-error --connect-timeout 3 --max-time 10 https://localhost:8081/_explorer/emulator.pem
```

Cosmos bootstrap waits up to about five minutes for its local certificate, installs it,
and then verifies HTTPS normally. Only the initial local certificate fetch bypasses verification.
The command above must succeed without `--insecure`.
The developer container shares the emulator network namespace for direct-mode TCP access through `localhost`.
Only the selected provider's database and developer service start.
The same build, test, R4 environment loader, metadata probe, and application-stop commands apply.
Build inside the Cosmos developer container before launching FHIR there.

Before stopping Cosmos, repeat the host ownership checks using its returned container ID
and expected service `fhir-cosmos` instead of `fhir-sql`.
Then use the matching host command:

```bash
docker compose --project-name "$project" -f .devcontainer/docker-compose.cosmos.yml stop fhir-cosmos cosmos
```

```powershell
docker compose --project-name $project -f .devcontainer/docker-compose.cosmos.yml stop fhir-cosmos cosmos
if ($LASTEXITCODE -ne 0) { throw 'Project stop failed.' }
```

Cosmos retains data across stops, but recreating its container discards the local database.
Do not use teardown as a restart recipe.
This legacy emulator option does not change the provider retirement notice in the [repository README](../README.md).

## Use the debugger instead

In VS Code, choose **R4 dev container**, **STU3 dev container**, or **R5 dev container**.
The matching build task builds the web project before launching its .NET 11 output.
Startup writes private debugger environment files in `~/.fhir-devcontainer/`.
They select the active provider and a separate `FHIR_R4`, `FHIR_STU3`, or `FHIR_R5` database.
The profiles disable `launchSettings.json` selection, so its default Cosmos profile cannot override SQL.
Existing Windows integrated-security SQL profiles remain unchanged.

VS Code forwards FHIR HTTPS on port 44348 and, for Cosmos, the emulator explorer on port 8081.
Use the **Ports** view to find the local address.
Run one FHIR debug profile at a time.
Neither Compose configuration publishes database ports on all host interfaces.
Local SQL uses its self-signed server certificate. Cosmos uses the emulator's public development key.
Do not expose these auth-disabled services to an untrusted network.
Container trust does not change host-browser trust. Use your approved host certificate process, not a validation bypass.

After changing SDK pins, Dockerfile, feature settings, or bootstrap scripts, rebuild the developer container.
Closing the VS Code remote session stops both selected Compose services.
With the CLI, use the ownership-checked stop commands above.

## Diagnose startup failures without deleting state

Use the verified project name and matching Compose file.
For SQL, inspect bounded logs and health state from the host:

```text
docker compose --project-name PROJECT_NAME -f .devcontainer/docker-compose.sql.yml logs --tail 100 sql
docker compose --project-name PROJECT_NAME -f .devcontainer/docker-compose.sql.yml ps --all
```

Replace `PROJECT_NAME` with the verified name, not a guessed folder-derived value.
Inspect local logs before sharing them. Do not dump environment files or credentials.

Compose bounds initial SQL readiness. Reattachment waits up to about 100 seconds,
including the final query timeout, then exits with an actionable error.
For authentication or fingerprint errors, restore the original `.devcontainer/.local/sql-password`.
The configuration cannot recover a lost password from the volume.
Preserve the volume for manual recovery, or use a separate clone and unique project for a fresh database.

For Cosmos timeouts, inspect the `cosmos` service logs with `docker-compose.cosmos.yml`.
Check Docker CPU and memory allocation and Linux x64 mode.
For certificate failures, inspect lifecycle output. Rebuild if a bootstrap script changed.
For downloads or restore failures, check access and approved trust for the failing endpoint.
Never disable TLS verification or change SDK pins to hide a setup failure.

This guide does not establish native Linux credential ownership and restart behavior with a non-1000 UID,
ARM support, remote-daemon layouts, debugger UI behavior, or host-browser certificate trust.
