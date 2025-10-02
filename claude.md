# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Essential Build & Test Commands

**Building the solution:**
```bash
dotnet build --configuration Release
```

**Running unit tests:**
```bash
dotnet test **/*UnitTests/*.csproj --configuration Release
```

**Running a specific test project:**
```bash
dotnet test test/Microsoft.Health.Fhir.Core.UnitTests/Microsoft.Health.Fhir.Core.UnitTests.csproj
```

**Running E2E tests for a specific FHIR version:**
```bash
dotnet test test/Microsoft.Health.Fhir.R4.Tests.E2E/Microsoft.Health.Fhir.R4.Tests.E2E.csproj
```

**Running the FHIR server (R4 as example):**
```bash
dotnet run --project src/Microsoft.Health.Fhir.R4.Web/Microsoft.Health.Fhir.R4.Web.csproj
```

**Building specific framework target:**
```bash
dotnet build --configuration Release -f net8.0
```

## Architecture Overview

### Request Flow Pattern
The FHIR server uses a **MediatR-based Request/Response/Handler pattern**:

1. **Controllers** (in `Microsoft.Health.Fhir.Api`) receive HTTP requests
2. **MediatR** routes requests to appropriate handlers
3. **Handlers** (in `Microsoft.Health.Fhir.Core/Features/*/`) contain business logic
4. **DataStores** provide persistence abstraction (Cosmos DB or SQL Server)

Example flow for resource creation:
```
FhirController → Mediatr → CreateResourceHandler → AuthorizationService →
ResourceReferenceResolver → SearchIndexer → FhirDataStore → UpsertOutcome
```

### FHIR Version Support
The codebase supports multiple FHIR versions with separate implementations:
- **Stu3**: FHIR STU3 (3.0.x)
- **R4**: FHIR R4 (4.0.x)
- **R4B**: FHIR R4B (4.3.x)
- **R5**: FHIR R5 (5.0.x)

Each version has dedicated projects:
- `Microsoft.Health.Fhir.[Version].Api` - Version-specific API endpoints
- `Microsoft.Health.Fhir.[Version].Core` - Version-specific core logic
- `Microsoft.Health.Fhir.[Version].Web` - Version-specific web hosting

### Layer Architecture

1. **API Layer** (`Microsoft.Health.Fhir.Api`, `Microsoft.Health.Fhir.[Version].Api`)
   - RESTful API implementation per HL7 FHIR spec
   - Controllers, filters, middleware

2. **Core Logic Layer** (`Microsoft.Health.Fhir.Core`, `Microsoft.Health.Fhir.[Version].Core`)
   - Business logic and domain models
   - MediatR request handlers
   - Search, validation, authorization

3. **Persistence Layer**
   - `Microsoft.Health.Fhir.CosmosDb` - Azure Cosmos DB implementation
   - `Microsoft.Health.Fhir.SqlServer` - SQL Server implementation
   - Both implement common abstractions from Core

4. **Shared Components** (`Microsoft.Health.Fhir.Shared.*`)
   - Code shared across multiple FHIR versions
   - Common API components, validation, utilities

### Key Feature Areas

- **Search**: `src/Microsoft.Health.Fhir.Core/Features/Search/`
- **Operations**: `src/Microsoft.Health.Fhir.Core/Features/Operations/` (Export, Import, Reindex, BulkDelete, BulkUpdate, etc.)
- **Authorization**: `src/Microsoft.Health.Fhir.Core/Features/Security/`
- **Validation**: `src/Microsoft.Health.Fhir.Core/Features/Validation/`
- **Compartments**: `src/Microsoft.Health.Fhir.Core/Features/Compartment/`

## Coding Standards

### Naming Conventions
- **PascalCase**: Classes, methods, public properties
- **camelCase with underscore prefix**: Private fields (`_fieldName`)
- XML documentation required for all public members
- Add blank line between namespace and class declarations

### Testing Requirements
- **Always build and fix errors first** before running tests
- Use **xUnit** framework
- Follow **Arrange-Act-Assert** pattern
- Use **NSubstitute** for mocking
- Add E2E tests for user-facing features
- Unit test file structure mirrors source: `src/Foo/Bar.cs` → `src/Foo.UnitTests/BarTests.cs`

## SQL Schema Migrations

Only needed for database structure changes (not for Core/API/Web logic changes).

**When adding a new SQL schema version:**

1. Increment version in `SchemaVersion.cs`
2. Update max version in `SchemaVersionConstants.cs`
3. Define SQL in schema files under `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/`:
   - Tables: `./Tables/*.sql`
   - Stored procedures: `./Sprocs/*.sql`
4. Create migration file: `./src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Migrations/{Version}.diff.sql`
   - The full snapshot `{Version}.sql` is auto-generated during build
5. Update `Microsoft.Health.Fhir.SqlServer.csproj` with new `LatestSchemaVersion` property
6. Add new SQL data store for the functionality specifying the new version
7. Add integration tests for SQL changes
8. Follow guidelines in `docs/SchemaVersioning.md`

## Async Operations & Background Jobs

Bulk operations (Import, Export, Reindex, BulkDelete, BulkUpdate) use async job infrastructure.

To create a new async operation, use the generator:
```powershell
.\tools\AsyncJobGenerator\GenerateAsyncJob -OperationName "Operation Name" -System -ResourceType
```

**Note:** Generated files are templates and require manual completion. Update `Microsoft.Health.Fhir.Shared.Api.projitems` manually.

## Documentation Resources

- **ADRs**: `docs/arch/` - Architectural Decision Records explaining design choices
- **ADR Template**: `docs/arch/Readme.md` - Instructions for creating ADRs
- **Flow Diagrams**: `docs/flow diagrams/` - Mermaid diagrams showing request flows
- **REST Samples**: `docs/rest/` - Sample FHIR HTTP requests
- **Schema Versioning**: `docs/SchemaVersioning.md` - Cosmos DB schema migration details

When creating ADRs, thoroughly analyze states, behaviors, outcomes, and edge cases. Reference existing ADRs and the FHIR specification.

## SQL Server Data Provider Architecture

The SQL Server data provider translates FHIR search queries into optimized T-SQL using a sophisticated multi-layer architecture.

### 🤖 Using the SQL Provider Agent

**For detailed guidance on SQL provider work, use the specialized agent:**

```
@sql-provider-agent <your question or task>
```

The agent has deep expertise in:
- Expression tree analysis and transformations
- Rewriter pipeline optimization
- Query generation strategies
- Schema version management
- Performance optimization
- Debugging SQL-related issues

### Architecture Overview

The SQL provider has **5 key layers**:

1. **Core Expression System** (`Microsoft.Health.Fhir.Core/Features/Search/Expressions/`)
   - Abstract expression tree representing FHIR queries (data store independent)
   - Visitor pattern for traversal: `IExpressionVisitor<TContext, TOutput>`
   - Expression types: SearchParameter, Binary, String, Multiary, Chained, Include, etc.

2. **SQL-Specific Extensions** (`Microsoft.Health.Fhir.SqlServer/Features/Search/Expressions/`)
   - `SqlRootExpression` - Root of SQL expression tree
   - `SearchParamTableExpression` - Queries over search parameter tables
   - `ISqlExpressionVisitor` - SQL-specific visitor interface

3. **Rewriter Pipeline** (`Microsoft.Health.Fhir.SqlServer/Features/Search/Expressions/Visitors/`)
   - Transforms and optimizes expression trees
   - **Order matters!** Defined in `SqlServerSearchService.cs`
   - Key rewriters: ChainFlattening, Sort, PartitionElimination, PredicatePushdown

4. **Query Generators** (`Microsoft.Health.Fhir.SqlServer/Features/Search/Expressions/Visitors/QueryGenerators/`)
   - `SqlQueryGenerator` - Master orchestrator
   - Type-specific generators for each search parameter type
   - Factory pattern for generator selection

5. **Schema Management** (`Microsoft.Health.Fhir.SqlServer/Features/Schema/`)
   - Schema versioning with feature flags (`SchemaVersionConstants`)
   - Tables, stored procedures, user-defined types
   - Migration files for incremental changes

### Search Query Flow

```
HTTP FHIR Request
  → FhirController (MediatR)
  → ExpressionParser (creates Core Expression tree)
  → SqlServerSearchService (applies rewriter pipeline)
  → SqlQueryGenerator (generates T-SQL)
  → SqlServerFhirDataStore (executes)
  → SearchResult
```

### Working with SQL Provider

#### Quick Tips

**Adding new search parameter:**
```
@sql-provider-agent How do I add support for composite search parameter X?
```

**Debugging slow queries:**
```
@sql-provider-agent Why is this chained search slow: Patient?organization.name=Acme
Analyze expression tree, rewriters, and generated SQL.
```

**Performance optimization:**
```
@sql-provider-agent Optimize: Patient?_lastUpdated=gt2024&gender=male
Show predicate pushdown opportunities and ideal SQL.
```

**Schema changes:**
```
@sql-provider-agent I need schema V97 for feature X. What's the process?
```

#### Critical Patterns

**Rewriter Order** - Never change without analysis:
1. ChainFlatteningRewriter
2. SortRewriter
3. PartitionEliminationRewriter
4. TopRewriter
5. ResourceColumnPredicatePushdownRewriter (major performance!)
6. (Others as needed)

**Schema Version Checks** - Always verify compatibility:
```csharp
if (_schemaInformation.Current >= SchemaVersionConstants.FeatureName)
{
    // Use new feature
}
```

**Expression Immutability** - Rewriters return new expressions (never modify in place)

**Query Plan Reuse** - `HashingSqlQueryParameterManager` caches plans based on structure hash

#### Key Files

- **Expression Base**: `Core/Features/Search/Expressions/Expression.cs`
- **SQL Root**: `SqlServer/Features/Search/Expressions/SqlRootExpression.cs`
- **Rewriter Base**: `SqlServer/.../Visitors/SqlExpressionRewriter.cs`
- **Query Generator**: `SqlServer/.../QueryGenerators/SqlQueryGenerator.cs`
- **Schema Versions**: `SqlServer/Features/Schema/SchemaVersionConstants.cs`
- **Orchestration**: `SqlServer/Features/Search/SqlServerSearchService.cs`

### Documentation

- **Agent Guide**: `.claude/SQL-PROVIDER-GUIDE.md` - Usage examples
- **Architecture**: `.claude/SQL-PROVIDER-ARCHITECTURE.md` - Visual diagrams

**Remember**: Use `@sql-provider-agent` for deep architectural guidance!

## Important Project Context

- This is the **Microsoft FHIR Server for Azure** - an open-source HL7 FHIR implementation
- Must strictly comply with **HL7 FHIR specifications**
- Supports both **Azure Cosmos DB** and **SQL Server** as data stores
- Prioritize **security, compliance, maintainability, and performance**
- Built with dependency injection and interface-based design for testability