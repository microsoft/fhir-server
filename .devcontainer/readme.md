# Develop FHIR Server in a container

Use the dev container to build and debug FHIR Server with the local Cosmos DB emulator.
The Ubuntu 24.04 developer container installs the SDKs pinned in `global.json` and
`build/dotnet10-compat/global.json`. .NET 11 is the primary SDK. .NET 10 remains available
for compatibility builds.

## Prepare your machine

- Install [Visual Studio Code](https://code.visualstudio.com/) and its
  [Dev Containers extension](https://marketplace.visualstudio.com/items?itemName=ms-vscode-remote.remote-containers).
- Install Docker with Compose v2 or later. On Windows, use Docker Desktop in Linux container mode.
- Use an x64 host. The classic Linux Cosmos DB emulator requires `linux/amd64`.
  This setup does not support ARM hosts or the vNext emulator.
- Give Docker at least two CPUs and enough memory for the emulator's 3 GB limit plus builds.
- Allow access to MCR, GHCR, Ubuntu package repositories, .NET downloads, and the feeds in `nuget.config`.
  If your network intercepts TLS, follow your organization's approved container certificate setup.
  Do not disable TLS validation.

The Docker-outside-of-Docker feature gives `vscode` access to the host Docker daemon.
Use this setup only with trusted repository code.

## Start the dev container

1. Open the repository folder in VS Code.
2. Run **Dev Containers: Reopen in Container**.
3. Wait for the image build and **Running postStartCommand** to finish.
   The command waits up to about five minutes for Cosmos DB, installs its certificate
   in the container trust store, verifies HTTPS, and creates the `vscode` ASP.NET development certificate.
4. Open a container terminal and check the SDKs and Docker access.

```bash
dotnet --list-sdks
dotnet --version
docker version
curl --fail https://localhost:8081/_explorer/emulator.pem
```

The SDK list must include both pinned versions, and `dotnet --version` must select .NET 11.
The curl command must succeed without `--insecure`.
The developer container shares the emulator's network namespace, so the Cosmos endpoint
and its direct-mode ports remain reachable through `localhost`.

After changing the SDK pins, Dockerfile, feature settings, or bootstrap script,
run **Dev Containers: Rebuild Container**. Restarting the container reruns certificate bootstrap,
including after the emulator certificate changes.
Closing the VS Code remote session stops both Compose services.
The emulator keeps data across stops, but recreating its container discards the local database.

## Build and debug

Run these commands in the container terminal.

```bash
dotnet build src/Microsoft.Health.Fhir.R4.Web/Microsoft.Health.Fhir.R4.Web.csproj
dotnet test --project src/Microsoft.Health.Fhir.Core.UnitTests/Microsoft.Health.Fhir.Core.UnitTests.csproj --framework net11.0 -- --filter-class Microsoft.Health.Fhir.Core.UnitTests.Models.PartialDateTimeTests
dotnet build src/Microsoft.Health.Fhir.R4.Web/Microsoft.Health.Fhir.R4.Web.csproj --framework net10.0
```

Use **Run and Debug** to select **R4 Cosmos DB**, **STU3 Cosmos DB**, or **R5 Cosmos DB**.
The matching build task builds the web project before launching its .NET 11 output.
The profiles use the emulator's public development key and disable FHIR authentication.
Do not expose these services to an untrusted network.
The SQL Server profiles still require a separately configured SQL Server and are not part of this Compose project.

VS Code forwards FHIR HTTPS on port 44348 and the emulator explorer on port 8081.
Use the **Ports** view to find the actual local addresses if either port is already in use.
Request `https://localhost:44348/metadata` or open `https://localhost:8081/_explorer/index.html`.
Run one FHIR debug profile at a time because they share port 44348.

The container trust store does not change your host browser's trust.
Trust the ASP.NET development certificate and emulator certificate on the host only through
your approved local-development process.
Do not add a global certificate-validation bypass.

## Troubleshoot startup

If bootstrap times out, open **Dev Containers: Show Container Log** and find the
`--project-name` value in its Compose commands. Use that project name from a host
terminal at the repository root to inspect the emulator logs.

```bash
docker compose --project-name <project-name> -f .devcontainer/docker-compose.yml logs cosmos
```

Check Docker's CPU and memory allocation and confirm that it runs Linux x64 containers.
Restart the dev container after fixing the emulator.
For certificate installation or HTTPS verification errors, inspect the lifecycle output
and rebuild if the bootstrap script changed.

If an image, SDK, feature, or NuGet download fails, check proxy access and certificate trust
for the failing endpoint. Keep TLS validation enabled.
If `dotnet` selects an old SDK, rebuild and compare `dotnet --list-sdks` with both JSON pins.
Do not share `bin` and `obj` outputs between Windows host builds and Linux container builds.
Use a separate checkout for host builds, or clean the affected outputs before switching environments.
