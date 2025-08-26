# FHIR Server DevContainers

## 🚀 New Organized DevContainer Setup

**We now have 3 specialized DevContainer configurations!** Each optimized for different development scenarios:

- 📁 **[github-sql/](./github-sql/)** - GitHub CI/CD with SQL Server backend
- 📁 **[github-cosmos/](./github-cosmos/)** - GitHub CI/CD with Cosmos DB backend  
- 📁 **[local-vscode/](./local-vscode/)** - Local development connecting to host databases

👉 **[See the comprehensive README.md for detailed instructions](./README.md)**

## Legacy Setup (Still Available)

[DevContainers](https://code.visualstudio.com/docs/remote/containers) lets you use a Docker container as a full-featured development environment. It allows you to open any folder inside (or mounted into) a container and take advantage of Visual Studio Code's full feature set.

The legacy devcontainer setup starts two containers:
1) the dev container with .NET SDK
2) a container with the SQL Server or Azure CosmosDB emulator

### Quick Start (Legacy)

1. Install the 'Visual Studio Code Remote - Containers' extension.
2. Open the repository root in VSCode  
3. VSCode will ask to open the folder inside a container. Allow it.
4. Your dev env is ready for use.

Alternative method:
1. Install the 'Visual Studio Code Remote - Containers' extension.
2. Click on the bottom left corner and click 'reopen in a container'
3. The container will start

### Running / Debugging FHIR

1. Run the selected profile (F5 or Run panel)
2. Use Postman or REST Client to query the server: `https://localhost:44348/Patient`

---

## ⚡ Upgrade to New DevContainers

**Benefits of the new setup:**
- ✅ No hardcoded passwords  
- ✅ Environment variable configuration
- ✅ Better organization for CI vs local development
- ✅ GitHub Copilot ready out of the box
- ✅ Optimized performance for each scenario

**[Get started with the new DevContainers →](./README.md)**