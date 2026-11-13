# Develop FHIR Server in a container

Use the default dev container to build and debug FHIR Server with SQL Server 2025.
Select the Cosmos DB configuration when you need the local emulator instead.
The Ubuntu 24.04 developer container installs the SDKs pinned in `global.json` and
`build/dotnet10-compat/global.json`. .NET 11 is the primary SDK. .NET 10 remains available
for compatibility builds.

## Prepare your machine

- Install [Visual Studio Code](https://code.visualstudio.com/) and its
  [Dev Containers extension](https://marketplace.visualstudio.com/items?itemName=ms-vscode-remote.remote-containers).
- Install Docker with Compose v2 or later. On Windows, use Docker Desktop in Linux container mode.
- Use a regular clone for container-side Git. The Linux container cannot follow
  a Windows worktree's external `.git` path.
- Use an x64 host with at least two CPUs and enough Docker memory for the database and builds.
  SQL Server requires at least 2 GB. The Cosmos emulator has a 3 GB limit.
  This setup does not support ARM hosts or the vNext emulator.
- Allow access to MCR, GHCR, Ubuntu package repositories, .NET downloads, and the feeds in `nuget.config`.
  If your network intercepts TLS, follow your organization's approved container certificate setup.
  Do not disable TLS validation.

The Docker-outside-of-Docker feature gives `vscode` access to the host Docker daemon.
Use this setup only with trusted repository code.

## Start SQL Server

1. Open the repository folder in VS Code.
2. Run **Dev Containers: Reopen in Container** and select **FHIR SQL Server** if prompted.
3. Wait for the image build and **Running postStartCommand** to finish.
   Compose waits for an authenticated SQL query before starting the developer container.
   The lifecycle command checks SQL again on every start and creates the `vscode` ASP.NET development certificate.
4. Open a container terminal and check the SDKs and Docker access.

```bash
dotnet --list-sdks
dotnet --version
docker version
```

The SDK list must include both pinned versions, and `dotnet --version` must select .NET 11.
Initialization creates one random SQL password in `.devcontainer/.local/sql-password`.
Git and Docker build contexts exclude that directory.
Keep the file private and retain it with your database. Do not copy it into a tracked environment file
or paste it into startup commands, logs, or bug reports.
The SQL container reads the file at runtime, then runs SQL Server as `mssql`.
Compose configuration and Dev Containers logs do not contain the password.

SQL data lives in the project's `sql-data` Docker volume.
Starting, stopping, and rebuilding the developer container preserve the database.
The startup script rejects a credential that does not match the volume rather than changing the database password.
Do not delete the credential file while you keep the volume.

After changing the SDK pins, Dockerfile, feature settings, or bootstrap script,
run **Dev Containers: Rebuild Container**.
Closing the VS Code remote session stops both Compose services.

From a host terminal, the equivalent CLI commands are:

```bash
devcontainer up --workspace-folder .
devcontainer exec --workspace-folder . dotnet --version
```

## Select Cosmos DB instead

Close the SQL remote session before switching configurations.
Run **Dev Containers: Reopen in Container** and select **FHIR Cosmos DB**.
Its configuration is `.devcontainer/cosmos/devcontainer.json`.
With the CLI, select it explicitly:

```bash
devcontainer up --workspace-folder . --config .devcontainer/cosmos/devcontainer.json
devcontainer exec --workspace-folder . --config .devcontainer/cosmos/devcontainer.json dotnet --version
```

Only the selected database and its developer service start.
Both configurations use the same SDK image and Docker-outside-of-Docker feature.
Distinct developer service names prevent reusing the wrong provider's container.

Cosmos startup waits up to about five minutes for the local emulator certificate.
Only that initial certificate fetch bypasses TLS verification, and only for the local emulator endpoint.
The lifecycle command installs the certificate in the container trust store and then verifies HTTPS normally.
Check trust from the container terminal:

```bash
curl --fail https://localhost:8081/_explorer/emulator.pem
```

The command must succeed without `--insecure`.
The developer container shares the emulator's network namespace for direct-mode TCP access through `localhost`.
Restarting reruns certificate bootstrap, including after the emulator certificate changes.
The emulator keeps data across stops, but recreating its container discards the local database.

## Build and debug

Run these commands in the container terminal.

```bash
dotnet build src/Microsoft.Health.Fhir.R4.Web/Microsoft.Health.Fhir.R4.Web.csproj
dotnet test --project src/Microsoft.Health.Fhir.Core.UnitTests/Microsoft.Health.Fhir.Core.UnitTests.csproj --framework net11.0 -- --filter-class Microsoft.Health.Fhir.Core.UnitTests.Models.PartialDateTimeTests
dotnet build src/Microsoft.Health.Fhir.R4.Web/Microsoft.Health.Fhir.R4.Web.csproj --framework net10.0
```

Use **Run and Debug** to select **R4 dev container**, **STU3 dev container**, or **R5 dev container**.
The matching build task builds the web project before launching its .NET 11 output.
Startup writes private debugger environment files in `~/.fhir-devcontainer/`.
The files select the active provider and a separate `FHIR_R4`, `FHIR_STU3`, or `FHIR_R5` database.
SQL profiles allow database creation, initialize the schema, and apply schema updates when you start the app.
The profiles disable `launchSettings.json` selection, so its default Cosmos profile cannot override SQL.
The existing Windows integrated-security SQL profiles remain unchanged.

Both configurations disable FHIR authentication for local development.
SQL uses its local self-signed server certificate.
Cosmos uses the emulator's public development key.
Do not expose these services to an untrusted network.

VS Code forwards FHIR HTTPS on port 44348.
The Cosmos configuration also forwards the emulator explorer on port 8081.
Use the **Ports** view to find the actual local addresses if either port is already in use.
Request `https://localhost:44348/metadata` or open `https://localhost:8081/_explorer/index.html`.
Run one FHIR debug profile at a time because they share port 44348.
Neither configuration publishes database ports on all host interfaces.

The container trust store does not change your host browser's trust.
Trust the ASP.NET development certificate and emulator certificate on the host only through
your approved local-development process.
Do not add a global certificate-validation bypass.

## Stop, restart, and recover

Close the remote session to stop both services, then reopen it to restart.
For CLI sessions, find the `--project-name` value in the startup log.
Use that exact name from a host terminal at the repository root:

```bash
docker compose --project-name <project-name> -f .devcontainer/docker-compose.sql.yml stop
devcontainer up --workspace-folder .
```

Do not use `down --volumes` unless you intend to delete that project's SQL database.
For Cosmos, use `docker-compose.cosmos.yml` and pass its configuration to `devcontainer up`.

If SQL startup fails, inspect the database logs and health state:

```bash
docker compose --project-name <project-name> -f .devcontainer/docker-compose.sql.yml logs sql
docker compose --project-name <project-name> -f .devcontainer/docker-compose.sql.yml ps --all
```

Compose bounds the initial readiness check. Reattachment waits up to about 100 seconds,
including the last query timeout, then exits with an error.
For authentication or credential-fingerprint errors, restore the original `.devcontainer/.local/sql-password`.
If that file is lost, the configuration cannot recover the password from the volume.
Keep the volume for manual recovery, or use a separate clone and Compose project for a fresh database.

For Cosmos timeouts, inspect the `cosmos` service logs with `docker-compose.cosmos.yml`.
Check Docker's CPU and memory allocation and confirm that it runs Linux x64 containers.
For certificate installation or HTTPS verification errors, inspect the lifecycle output.
Rebuild if a bootstrap script changed.

If an image, SDK, feature, or NuGet download fails, check proxy access and certificate trust
for the failing endpoint. Keep TLS validation enabled.
If `dotnet` selects an old SDK, rebuild and compare `dotnet --list-sdks` with both JSON pins.
Do not share `bin` and `obj` outputs between Windows host builds and Linux container builds.
Use a separate checkout for host builds, or clean the affected outputs before switching environments.
