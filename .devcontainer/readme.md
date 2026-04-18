## Dev Containers

This repository ships multiple Dev Container variants under `.devcontainer/`:

- `SQL/` - FHIR server plus SQL Server
- `Cosmos/` - FHIR server plus Azure Cosmos DB emulator
- `Lite/` - FHIR server only

Shared assets such as the base `Dockerfile` and lifecycle scripts stay at the root of `.devcontainer/`.

### Prerequisites

- Visual Studio Code with the **Dev Containers** extension
- Docker Desktop or another Docker engine compatible with Dev Containers

### Opening a variant

1. Open the repository in Visual Studio Code.
2. Run **Dev Containers: Reopen in Container**.
3. Choose the variant you want from the picker.

VS Code will discover the `devcontainer.json` files in the variant folders and let you choose between SQL, Cosmos, and Lite.

### Variant notes

#### SQL

- Starts SQL Server and the dev container on the same Docker network.
- The dev container reaches SQL Server at `sql:1433`.
- The host reaches SQL Server on forwarded port `1433`.

#### Cosmos

- Uses the Linux-based Azure Cosmos DB emulator container on `mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview`.
- Supports x64 and ARM64 machines, including Apple Silicon Macs.
- Starts the emulator in HTTPS mode and installs the emulator certificate into the dev container during creation.
- Forwards the emulator endpoint on `8081` over HTTPS and the explorer UI on `1234` over HTTP.
- Current emulator limitations still apply, including the documented NoSQL API and gateway-mode constraints.

#### Lite

- Starts only the dev container and relies on the usual forwarded application ports.

### Running and debugging

Use the existing launch configurations in `.vscode/launch.json` after the container is ready.

The default FHIR endpoint is available at `https://localhost:44348`.

### Manual validation

#### SQL

1. Reopen the repository in the `SQL` dev container.
2. Run one of the SQL launch configurations from `.vscode/launch.json`.
3. Verify the FHIR server responds on `https://localhost:44348`.
4. Verify SQL Server is reachable on `localhost:1433`.

#### Cosmos

1. Reopen the repository in the `Cosmos` dev container.
2. Wait for the post-create certificate installation to finish.
3. Run one of the Cosmos launch configurations from `.vscode/launch.json`.
4. Verify the emulator responds on `https://localhost:8081`.
5. Verify the explorer UI is reachable on `http://localhost:1234`.
6. Verify the FHIR server responds on `https://localhost:44348`.
