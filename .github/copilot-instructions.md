# GitHub Copilot Instructions for Microsoft FHIR Server

Welcome to the Microsoft FHIR Server! These guidelines help GitHub Copilot provide relevant, accurate, and context-sensitive suggestions to enhance contributions to this project.

---

## General Guidelines

- Follow the project's coding standards, which adhere to C#, .NET conventions, and Azure development best practices.
- Ensure solutions strictly align with Fast Healthcare Interoperability Resources (FHIR) standards.
- Prioritize security, compliance, maintainability, and performance in all implementations.
- Search and consult the online FHIR Standards documentation if you're unsure about any specification issue: https://www.hl7.org/fhir/
- If you write code you should:
  - Always build and fix any errors.
  - Add a unit test.
  - Add an E2E test when relevant.

---

## Project Structure

Here's a high-level overview of key directories and their purposes. Consider how each component relates to typical user stories or feature implementations:

- **.devcontainer/**: Development container configurations ensuring consistent environments.
- **.github/**: GitHub-specific workflows, issue templates, and configurations.
- **.vscode/**: Visual Studio Code settings.
- **build/**: Scripts and tools for Azure DevOps Pipelines (YAML).
- **docs/**: Project documentation, including setup guides and architectural diagrams.
  - **arch/**: Project documentation, Archtectural Design Records for understanding design decisions.
  - **arch/Readme.md: ADR instructions and template.
  - **rest/**: Sample FHIR HTTP Rest requests demonstrating various FHIR functionality.
  - **flow diagrams/**: Mermaid diagrams to understand the project's architecture, layers, and request flows.
- **release/**: Release assets and notes.
- **samples/**: Sample code illustrating FHIR server usage.
- **src/**: Primary source code of the application.
  - **Microsoft.Health.Fhir.Api/**: RESTful API layer conforming to HL7 FHIR specs (controllers, filters, actions).
  - **Microsoft.Health.Fhir.Core/**: Core FHIR domain logic and models.
  - **Microsoft.Health.Fhir.CosmosDb/**: Azure Cosmos DB data persistence logic.
  - **Microsoft.Health.Fhir.SqlServer/**: SQL Server data persistence logic.
    - **Features/Schema/**: SQL schema files, automatically generated snapshots, and manually crafted diff scripts.
  - **Microsoft.Health.Fhir.Shared.Api/**: Components shared across multiple FHIR API versions.
  - **Microsoft.Health.Fhir.Shared.Core/**: Core components shared across multiple FHIR versions.
  - **Microsoft.Health.Fhir.[Stu3|R4|R5].Core/**: Version-specific FHIR core components.
  - **Microsoft.Health.Fhir.[Stu3|R4|R5].Api/**: Version-specific FHIR API implementations.
  - **Microsoft.Health.Fhir.[Stu3|R4|R5].Web/**: Version-specific FHIR Web hosting layer.
  - **UnitTests/** directories: Contain unit tests corresponding to each respective component.
  - **Tests.E2E/** directories: End-to-end integration tests for FHIR implementations.
- **tools/**: Auxiliary utilities for development and maintenance tasks.

---

## Coding Conventions

- Adhere to clear, explicit naming conventions:
  - Use PascalCase for classes, methods, public properties.
  - Use camelCase for private fields and local variables.
- Follow modern .NET best practices, including asynchronous programming (`async`/`await`).
- Implement defensive programming:
  - Validate inputs rigorously.
  - Handle exceptions gracefully, avoiding unhandled exceptions in runtime.

---

## Architectural Guidance

- Maintain clear separation of concerns across layers: API, Business Logic, Data Access, and Infrastructure.
- Design implementations for modularity, extensibility, and scalability.
- Use dependency injection and interface-based designs for improved testability and loose coupling.
- Utilize the Request/Response/Handler pattern based on the .NET Mediatr library.
- Clearly link architectural decisions back to user stories or business requirements to enhance traceability and clarity.
- When creating an ADR, please think thoroughly through possible states, behaviors, outcomes and edgecases. Describe how the change may impact each of these. Use background information from the codebase, previous ADRs and the FHIR Specification. If a decision makes a previous ADR obselete you should mark it as such.
---

## Testing

- Include comprehensive unit and integration tests with each contribution.
- Utilize existing testing frameworks and libraries (xUnit, Moq).
- Ensure tests are detailed with meaningful assertions to verify functionality.
- Explicitly relate tests to user story acceptance criteria to reinforce the connection between testing and user requirements.

---

## Documentation

- Provide clear inline XML documentation comments (`///`) for public methods and complex implementations.
- Include detailed explanations and context within pull requests for any significant logic or architectural changes.
- Encourage documenting references to user stories or acceptance criteria within pull requests or commit messages to improve traceability.

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

By following these guidelines, your contributions will maintain the project's high standards for consistency, security, compliance, and performance.

Happy coding!

