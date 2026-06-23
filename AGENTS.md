# AGENTS.md — Microsoft FHIR Server

Universal agent instructions for all AI tools working in this repository.

---

## Repository Overview

This is the **Microsoft FHIR Server** — an open-source, standards-compliant implementation of HL7 FHIR (Fast Healthcare Interoperability Resources) built on .NET and Azure. It supports both **Azure Cosmos DB** and **SQL Server** as storage backends, and exposes a RESTful API conforming to FHIR R4/R5/STU3.

---

## Coding Conventions

- **C# naming**: PascalCase for classes, methods, public properties. `_camelCase` for private fields.
- **XML doc** on all public members.
- **One blank line** between `namespace` and `class` declarations.
- **Dependency injection** and interface-based design throughout — never `new` a service directly.
- **MediatR** Request/Response/Handler pattern for all business operations.
- **FHIR compliance first** — any behavior change must be validated against the HL7 FHIR specification.

---

## Testing Requirements

- **Build and fix all errors before writing tests.**
- **xUnit** as the testing framework; **NSubstitute** for mocks.
- **Arrange-Act-Assert** pattern in every test.
- Unit tests for all new functionality; E2E tests when the feature touches the HTTP layer.
- Validate inputs rigorously; handle exceptions gracefully with no unhandled runtime exceptions.

---

## Project Structure

| Path | Purpose |
|------|---------|
| `src/Microsoft.Health.Fhir.Api/` | RESTful API layer (controllers, filters, actions) |
| `src/Microsoft.Health.Fhir.Core/` | Core FHIR domain logic and models |
| `src/Microsoft.Health.Fhir.SqlServer/` | SQL Server data persistence |
| `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/` | SQL schema, migrations, stored procs |
| `src/Microsoft.Health.Fhir.CosmosDb/` | Cosmos DB data persistence |
| `src/Microsoft.Health.Fhir.Shared.*/` | Components shared across FHIR versions |
| `src/Microsoft.Health.Fhir.[Stu3\|R4\|R5].*/` | Version-specific implementations |
| `docs/arch/` | Architectural Decision Records (ADRs) |
| `docs/flow diagrams/` | Mermaid architecture and request-flow diagrams |
| `tools/AsyncJobGenerator` | Scaffold tool for new background job types |

---

## Architectural Principles

- Strict separation of concerns: **API → Business Logic → Data Access → Infrastructure**.
- All SQL work lives in `Microsoft.Health.Fhir.SqlServer`; keep Core/API layers SQL-agnostic.
- ADRs live in `docs/arch/`. If a change makes a prior ADR obsolete, update it.
- Clearly link code changes back to the user story or requirement that motivated them.

---

## Security and Compliance

- Never commit secrets or credentials.
- Follow Azure security best practices: encrypted transport, least-privilege identity, no plaintext config.
- All data access must respect FHIR resource-level authorization.

---

## Available Agents (`.github/agents/`)

| Agent | Use when |
|-------|---------|
| `coding-agent` | General implementation, delegates to fast/complex as needed |
| `complex-coding-agent` | Complex multi-file refactors, threading, race conditions |
| `fast-coding-agent` | Single-file edits, simple fixes, build errors |

## Available Skills (`.github/skills/`)

| Skill | Use when |
|-------|---------|
| `create-adr` | Drafting an Architectural Decision Record |
