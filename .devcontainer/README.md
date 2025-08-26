# FHIR Server DevContainers

This directory contains three different DevContainer configurations optimized for different development scenarios:

## 🚀 Quick Start

### Prerequisites

- Visual Studio Code with the Remote-Containers extension
- Docker Desktop or Docker Engine
- For local development: SQL Server or Cosmos DB Emulator running on your host

### Choose Your Configuration

1. **GitHub CI - SQL Server** (`github-sql/`) - For CI/CD and automated testing with SQL Server
2. **GitHub CI - Cosmos DB** (`github-cosmos/`) - For CI/CD and automated testing with Cosmos DB
3. **Local Development** (`local-vscode/`) - For local development connecting to host databases

## 📁 Container Configurations

### 1. GitHub CI - SQL Server (`github-sql/`)

**Purpose**: Optimized for GitHub Actions, automated builds, and CI/CD with SQL Server backend.

**Features**:
- ✅ SQL Server 2022 container with health checks
- ✅ Environment variable based configuration (no hardcoded passwords)
- ✅ GitHub Copilot and C# development tools
- ✅ Optimized for automated testing and builds
- ✅ Named volumes for better CI performance
- ✅ Network isolation

**Environment Variables**:
- `SQL_SERVER_PASSWORD` - SQL Server SA password (default: Dev123!@#)
- `FHIR_SERVER_SECRET` - Application secret key

### 2. GitHub CI - Cosmos DB (`github-cosmos/`)

**Purpose**: Optimized for GitHub Actions, automated builds, and CI/CD with Cosmos DB backend.

**Features**:
- ✅ Azure Cosmos DB Emulator with SSL certificate handling
- ✅ Environment variable based configuration
- ✅ GitHub Copilot and C# development tools
- ✅ Automatic SSL certificate installation
- ✅ Optimized for automated testing and builds
- ✅ Higher memory allocation for Cosmos DB Emulator

**Environment Variables**:
- `COSMOS_DB_KEY` - Cosmos DB access key (uses emulator default)
- `FHIR_SERVER_SECRET` - Application secret key

### 3. Local Development (`local-vscode/`)

**Purpose**: For local development using existing SQL Server or Cosmos DB instances on your host machine.

**Features**:
- ✅ Connects to host database instances (no containers needed)
- ✅ Flexible DataStore switching via environment variables
- ✅ GitHub Copilot and full C# development suite
- ✅ Additional local development extensions
- ✅ Host networking for easy database access
- ✅ Authentication enabled (more realistic development)

**Environment Variables**:
- `DATASTORE_TYPE` - "SqlServer" or "CosmosDb" (default: SqlServer)
- `SQL_SERVER_CONNECTION` - SQL Server connection string
- `COSMOS_DB_HOST` - Cosmos DB endpoint URL
- `COSMOS_DB_KEY` - Cosmos DB access key
- `FHIR_SERVER_SECRET` - Application secret key

## 🔧 Usage Instructions

### Using GitHub CI Containers

1. Choose either `github-sql` or `github-cosmos`
2. Create a `.env` file in the chosen directory (optional):
   ```bash
   SQL_SERVER_PASSWORD=YourSecurePassword123!
   FHIR_SERVER_SECRET=your-secret-key
   ```
3. Open folder in VSCode
4. Select "Reopen in Container" when prompted
5. Wait for container to build and start
6. Use F5 to run/debug the FHIR server

### Using Local Development Container

1. Ensure SQL Server or Cosmos DB is running on your host:
   - **SQL Server**: Available at `localhost` with integrated security or connection string
   - **Cosmos DB**: Emulator running at `https://localhost:8081`

2. Create a `.env` file in `local-vscode/` directory:
   ```bash
   DATASTORE_TYPE=SqlServer
   # OR
   DATASTORE_TYPE=CosmosDb
   
   # SQL Server (if using)
   SQL_SERVER_CONNECTION=server=host.docker.internal;Initial Catalog=FHIR;Integrated Security=true;TrustServerCertificate=true
   
   # Cosmos DB (if using)  
   COSMOS_DB_HOST=https://host.docker.internal:8081
   COSMOS_DB_KEY=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==
   ```

3. Open the `local-vscode` folder in VSCode
4. Select "Reopen in Container" when prompted
5. Use F5 to run/debug the FHIR server

## 🛠 Development Features

All containers include:

- ✅ **GitHub Copilot** - AI-powered code completion and chat
- ✅ **C# Dev Kit** - Full C# development experience
- ✅ **SQL Server Extension** - Database management and queries
- ✅ **Cosmos DB Extension** - Cosmos DB management
- ✅ **FHIR Tools** - FHIR-specific development extensions
- ✅ **REST Client** - Test FHIR API endpoints
- ✅ **Azure CLI & GitHub CLI** - Cloud and GitHub integration

## 🚀 Launch Configurations

Each container includes pre-configured launch profiles accessible via F5 or the Run panel:

- **R4 SQL Server** - FHIR R4 with SQL Server backend
- **R4 Cosmos DB** - FHIR R4 with Cosmos DB backend  
- **STU3/R5 variants** - Additional FHIR versions

## 📊 Performance Considerations

### GitHub CI Containers
- Use named volumes for caching
- Optimized build contexts
- Health checks for database readiness
- Minimal resource allocation for CI efficiency

### Local Development Container
- Host networking for database access
- File watcher optimizations
- Larger cache volumes
- Full development tooling

## 🔒 Security Best Practices

- ✅ No hardcoded passwords in configuration files
- ✅ Environment variable based secrets
- ✅ Network isolation in CI containers
- ✅ Least privilege user (non-root)
- ✅ Secure defaults with override capabilities

## 🐛 Troubleshooting

### Common Issues

1. **Build Failures**: Ensure Docker has enough memory allocated (8GB+ recommended)
2. **Database Connection Issues**: Check firewall settings and connection strings
3. **Port Conflicts**: Ensure ports 44348, 1433, 8081 are available
4. **Certificate Issues**: Cosmos DB containers automatically handle SSL certificates

### Debugging

1. Check container logs: `docker compose logs -f`
2. Verify environment variables: Use terminal in VSCode container
3. Test database connectivity: Use SQL Server or Cosmos DB extensions
4. Check FHIR server health: Navigate to `https://localhost:44348/health`

## 📚 Additional Resources

- [FHIR Server Documentation](../../docs/)
- [DevContainer Specification](https://containers.dev/)
- [GitHub Copilot Documentation](https://docs.github.com/en/copilot)
- [FHIR Specification](https://www.hl7.org/fhir/)

---

**Happy FHIR Development! 🩺💻**