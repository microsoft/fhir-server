# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is the **FHIR Server for Azure**, an open-source .NET Core implementation of the HL7 Fast Healthcare Interoperability Resources (FHIR) specification. It provides a cloud-based FHIR service supporting multiple FHIR versions (STU3, R4, R4B, R5) with pluggable persistence layers (Azure Cosmos DB and SQL Server).

The architecture follows a layered design:
- **Hosting Layer**: Supports different environments with custom IoC configuration
- **RESTful API Layer**: Implementation of HL7 FHIR specification APIs
- **Core Logic Layer**: Core FHIR business logic and domain models
- **Persistence Layer**: Pluggable data persistence (Cosmos DB and SQL Server)

## Build and Development Commands

### Prerequisites
- .NET SDK 9.0.305 (specified in global.json)
- SQL Server (for SQL persistence) or Azure Cosmos DB emulator (for Cosmos persistence)
- Azurite storage emulator (for import/export operations)

### Building the Project
```bash
# Build the entire solution
dotnet build Microsoft.Health.Fhir.sln

# Build specific FHIR version (using solution filters)
dotnet build R4.slnf
dotnet build R5.slnf
```

### Running Tests
```bash
# Run all unit tests
dotnet test --filter "FullyQualifiedName~UnitTests"

# Run tests for a specific project
dotnet test src/Microsoft.Health.Fhir.Core.UnitTests/Microsoft.Health.Fhir.Core.UnitTests.csproj

# Run E2E tests for R4
dotnet test test/Microsoft.Health.Fhir.R4.Tests.E2E/Microsoft.Health.Fhir.R4.Tests.E2E.csproj

# Run integration tests
dotnet test test/Microsoft.Health.Fhir.R4.Tests.Integration/Microsoft.Health.Fhir.R4.Tests.Integration.csproj
```

### Running the FHIR Server Locally

The server can be run with different persistence backends using launch profiles:

```bash
# Run with Cosmos DB
dotnet run --project src/Microsoft.Health.Fhir.R4.Web --launch-profile CosmosDb

# Run with SQL Server
dotnet run --project src/Microsoft.Health.Fhir.R4.Web --launch-profile SqlServer

# Default profile (Cosmos DB)
dotnet run --project src/Microsoft.Health.Fhir.R4.Web
```

The server will be available at `https://localhost:44348/`

### Authentication for Local Development

The project includes a built-in development identity provider (OpenIddict-based). To obtain a token for local testing:

```bash
# Using curl
curl -X POST https://localhost:44348/connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "client_id=globalAdminServicePrincipal&client_secret=globalAdminServicePrincipal&grant_type=client_credentials&scope=fhir-api"
```

Credentials for development are defined in `testauthenvironment.json` (local/test only).

## Architecture and Code Structure

### Layered Architecture

The codebase follows a strict layered architecture enforced through project dependencies:

1. **Web Layer** (`Microsoft.Health.Fhir.[Version].Web`)
   - ASP.NET Core hosting, startup configuration
   - Entry points for R4, R4B, R5, STU3 versions
   - Launch settings in `Properties/launchSettings.json`

2. **API Layer** (`Microsoft.Health.Fhir.[Version].Api` and `Microsoft.Health.Fhir.Shared.Api`)
   - Controllers, filters, middleware
   - RESTful API implementation per FHIR spec
   - HTTP request/response handling

3. **Core Layer** (`Microsoft.Health.Fhir.[Version].Core` and `Microsoft.Health.Fhir.Shared.Core`)
   - Business logic, domain models
   - Uses **MediatR Request/Response/Handler pattern** extensively
   - Version-specific FHIR logic vs. shared cross-version logic

4. **Persistence Layer**
   - `Microsoft.Health.Fhir.SqlServer`: SQL Server implementation
   - `Microsoft.Health.Fhir.CosmosDb`: Azure Cosmos DB implementation
   - Abstraction through interfaces in Core layer

### Key Architectural Patterns

**MediatR Pattern**: The codebase extensively uses the Request/Response/Handler pattern via MediatR library. When tracing through code:
- Controllers send requests via `IMediator`
- Handlers in Core layer process requests (e.g., `CreateResourceHandler`, `SearchResourceHandler`)
- Flow: Controller → MediatR → Handler → DataStore

**Search Architecture**: Search is a critical component with three phases:
1. **Extraction**: Uses FHIRPath expressions to extract searchable values from resources (`SearchIndexer`)
2. **Persistence**: Values stored as search indices alongside resources
3. **Search**: Expression parsing converts FHIR search parameters to data store queries

See `docs/SearchArchitecture.md` for detailed search implementation.

**Dependency Injection**: Heavy use of DI throughout. Service registration happens in version-specific startup code.

### Important Subdirectories

- `src/Microsoft.Health.Fhir.Core/Features/`: Core features organized by capability (Search, Operations, Security, Validation, etc.)
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/`: SQL schema definitions and migrations
- `docs/flow diagrams/`: Mermaid diagrams showing request flows (create-resource.md, search-api.md, etc.)
- `docs/arch/`: Architecture Decision Records (ADRs) documenting design decisions
- `docs/rest/`: Sample HTTP requests demonstrating FHIR functionality
- `tools/`: Utility tools (AsyncJobGenerator, SchemaManager, DataSynth, etc.)

### FHIR Version Support

The codebase supports multiple FHIR versions through a "Shared" + "Specific" pattern:
- **Shared projects** (`.Shared.Core`, `.Shared.Api`, `.Shared.Web`): Code common across all FHIR versions
- **Version-specific projects**: Code unique to STU3, R4, R4B, or R5
- Use `.projitems` for sharing code between projects

## SQL Schema Migrations

When making database structure changes, SQL migrations are required:

1. **Increment version number** in `SchemaVersion.cs`
2. **Update Max version** in `SchemaVersionConstants.cs`
3. **Define SQL functionality** in schema definition files:
   - Tables: `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Tables/*.sql`
   - Stored procedures: `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Sprocs/*.sql`
4. **Create migration file**: `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Migrations/<Version>.diff.sql`
   - Full snapshot `<Version>.sql` is auto-generated by build tools
5. **Update csproj**: Set `LatestSchemaVersion` property in `Microsoft.Health.Fhir.SqlServer.csproj`
6. **Add data store methods** for the new functionality, specifying the required version
7. **Add integration tests** for SQL changes
8. **Follow guidelines** in `docs/SchemaVersioning.md`

Schema upgrades can be automatic (dev/test) or manual via Schema Manager tool (production).

## Testing Guidelines

- **Framework**: xUnit
- **Mocking**: NSubstitute for dependencies
- **Pattern**: Arrange-Act-Assert pattern
- **Test types**:
  - **Unit tests**: `*.UnitTests` projects (in `src/`)
  - **Integration tests**: `*.Tests.Integration` projects (in `test/`)
  - **E2E tests**: `*.Tests.E2E` projects (in `test/`)

Always add E2E tests when implementing user-facing features.

## Coding Standards

The project enforces standards via StyleCop.Analyzers, .editorconfig, and stylecop.json:

- **Naming conventions**:
  - PascalCase: classes, methods, public properties
  - camelCase with underscore prefix: private fields (`_fieldName`)
- **XML documentation** required for all public members
- **Newline** between namespace and class declaration
- **Defensive programming**: Validate inputs, handle exceptions gracefully
- **No emojis** in code or documentation unless explicitly requested

## Roles and Authorization

Authorization uses a role-based access control system defined in `src/Microsoft.Health.Fhir.Shared.Web/roles.json`:

**Data Actions**: `read`, `write`, `delete`, `hardDelete`, `export`, `resourceValidate`, `*` (all)

Role assignments are managed through the identity provider (Azure AD app roles in production, testauthenvironment.json locally).

## Architecture Decision Records (ADRs)

When making significant architectural decisions:
1. Create ADR under `docs/arch/` with format: `adr-<yymm>-<short-title>.md`
2. Use proposals folder `docs/arch/Proposals/` for proposed designs
3. Follow ADR template with sections: Title, Context, Decision, Status, Consequences
4. Reference previous ADRs and FHIR specification
5. Mark superseded ADRs appropriately (never delete)

## Asynchronous Operations

Background jobs (Import, Export, Reindex) use the async job framework. To create new async operations, use the tooling in `tools/AsyncJobGenerator`.

## Common Development Patterns

- **Creating resources**: Controller → MediatR → `CreateResourceHandler` → `AuthorizationService` → `ResourceReferenceResolver` → `ResourceWrapperFactory` → `SearchIndexer` → `FhirDataStore`
- **Search**: Controller → `SearchResourceHandler` → `ExpressionParser` → `QueryBuilder` → DataStore-specific query execution
- **Operations**: Custom FHIR operations in `src/Microsoft.Health.Fhir.Core/Features/Operations/` (Export, Import, Reindex, ConvertData, etc.)

## Useful Documentation

- `docs/HowToDebug.md`: Debugging with Source Link support
- `docs/SchemaMigrationGuide.md`: SQL schema upgrade process
- `docs/SearchArchitecture.md`: Search implementation details
- `docs/Roles.md`: Authorization model
- `docs/Authentication.md`: Auth configuration
- `docs/HowToConnectSQLDatabase.md`: SQL Server setup for development
