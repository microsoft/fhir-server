# Copilot Instructions for Microsoft FHIR Server

Welcome to the Microsoft FHIR Server! These guidelines help provide relevant, accurate, and context-sensitive suggestions to enhance contributions to this project.

---

## General Guidelines

- Follow the project's coding standards, which adhere to C#, .NET conventions, and Azure development best practices.
- Ensure solutions strictly align with Fast Healthcare Interoperability Resources (FHIR) standards.
- Prioritize security, compliance, maintainability, and performance in all implementations.

---

## Coding Conventions

- Adhere to clear, explicit naming conventions:
  - Use PascalCase for classes, methods, public properties.
  - Use camelCase for private fields and local variables, with an underscore prefix (_fieldName).
- Include XML documentation for all public members
- On creation of new class, ensure there is a new line between namespace and class declaration

---

## Testing Requirements
- Always build and fix any errors first.
- Write unit tests for all new functionality.
- Use xUnit for testing framework
- Follow Arrange-Act-Assert pattern in tests
- Use NSubstitute for external dependencies in tests.
- Add an E2E test when relevant.
- Implement defensive programming:
  - Validate inputs rigorously.
  - Handle exceptions gracefully, avoiding unhandled exceptions in runtime.

---

## Project Structure

Here's a high-level overview of key directories and their purposes. Consider how each component relates to typical user stories or feature implementations:

- **.devcontainer/**: Development container configurations ensuring consistent environments.
- **.github/**: GitHub-specific workflows, issue templates, and configurations.
- **.vscode/**: Visual Studio Code settings.
- **build/**: Scripts and tools for Azure DevOps Pipelines (YAML).
- **docs/**: Project documentation, including setup guides and architectural diagrams.
  - **arch/**: Project documentation, Architectural Design Records for understanding design decisions.
  - **arch/Readme.md**: ADR instructions and template.
  - **rest/**: Sample FHIR HTTP Rest requests demonstrating various FHIR functionality.
  - **flow diagrams/**: Mermaid diagrams to understand the project's architecture, layers, and request flows.
- **release/**: Release assets and notes.
- **samples/**: Sample code illustrating FHIR server usage.
- **src/**: Primary source code of the application.
  - **Microsoft.Health.Fhir.Api/**: RESTful API layer conforming to HL7 FHIR specs (controllers, filters, actions).
  - **Microsoft.Health.Fhir.Core/**: Core FHIR domain logic and models.
  - **Microsoft.Health.Fhir.CosmosDb/**: Azure Cosmos DB data persistence logic.
  - **Microsoft.Health.Fhir.SqlServer/**: SQL Server data persistence logic.
    - **Features/Schema/**: SQL schema files (see "SQL migration" when updates are needed).
  - **Microsoft.Health.Fhir.Shared.Api/**: Components shared across multiple FHIR API versions.
  - **Microsoft.Health.Fhir.Shared.Core/**: Core components shared across multiple FHIR versions.
  - **Microsoft.Health.Fhir.[Stu3|R4|R5].Core/**: Version-specific FHIR core components.
  - **Microsoft.Health.Fhir.[Stu3|R4|R5].Api/**: Version-specific FHIR API implementations.
  - **Microsoft.Health.Fhir.[Stu3|R4|R5].Web/**: Version-specific FHIR Web hosting layer.
  - **UnitTests/** directories: Contain unit tests corresponding to each respective component.
  - **Tests.E2E/** directories: End-to-end integration tests for FHIR implementations.
- **tools/**: Auxiliary utilities for development and maintenance tasks.

---

## Architectural Guidance

- Maintain clear separation of concerns across layers: API, Business Logic, Data Access, and Infrastructure.
- Design implementations for modularity, extensibility, and scalability.
- Use dependency injection and interface-based designs for improved testability and loose coupling.
- Utilize the Request/Response/Handler pattern based on the .NET Mediatr library.
- Clearly link architectural decisions back to user stories or business requirements to enhance traceability and clarity.
- When creating an ADR, please think thoroughly through possible states, behaviors, outcomes and edgecases. Describe how the change may impact each of these. Use background information from the codebase, previous ADRs and the FHIR Specification. If a decision makes a previous ADR obsolete you should update it as such.

---

## SQL migration (when needed)
- Only changes to the database structure need a sql migration, evaluate if the functionality is related to the database or in Core, API or Web which do not need these steps.
  - For adding new SQL schema version, follow these steps
    - Increment the version number in the SchemaVersion.cs file
    - Update the Max version in the SchemaVersionConstants.cs file
    - Define sql functionality in the schema definitional files under ./src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/**/*.sql.
      - Tables are in ./Tables/*.sql
      - Stored procs are in ./Sprocs/*.sql
    - Create a new migration file with a format `Version.diff.sql` in ./src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Migrations folder
      - The full snapshot file `Version.sql` will be generated automatically by a build tool, so you do not need to manually create this.
    - Update the Microsoft.Health.Fhir.SqlServer.csproj file to include the new version in LatestSchemaVersion property
  - Let the developer know that they need to add new sql data store for the corresponding functionality and specify the new version.
  - Let the developer know that they need to add new integration tests for the corresponding sql changes.
  - Let the developer know to follow SQL guidelines from ./docs/SchemaVersioning.md

---

## Other special functions
- Async Requests and Jobs in FHIR Server, Bulk operations such as Import, Export and Reindex use these.
  - To create background jobs (async requests) in FHIR, use the steps and tool provided in ./tools/AsyncJobGenerator

---

## Security and Compliance

- Follow Azure and industry-standard security best practices:
  - Handle secrets securely.
  - Maintain secure configurations.
  - Ensure proper data encryption and data privacy compliance.

---

## Performance

- Optimize for performance:
  - Efficient database querying and data handling.
  - Minimize response latency.
  - Implement caching strategically to improve efficiency.

---

Happy coding!

