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

## SQL Work — Use the Skills

SQL in this codebase is highly specialized. Before making any SQL change, **activate the relevant skill** from `.github/skills/`:

| Area | Skill to activate |
|------|------------------|
| Schema migrations (new version) | `fhir-sql-author-schema-migration` |
| Adding/modifying tables or indexes | `fhir-sql-add-table-or-index` |
| Writing or editing stored procedures | `fhir-sql-write-stored-procedure` |
| Background jobs (export/import/reindex) | `fhir-sql-background-jobs` |
| Resource CRUD lifecycle (MergeResources) | `fhir-sql-resource-crud-and-history` |
| Search-to-SQL query pipeline | `fhir-sql-query-generation-pipeline` |
| Diagnosing slow queries | `fhir-sql-diagnose-query-perf` |
| Hyperscale / read replicas / operations | `fhir-sql-production-operations` |

**Never write SQL for this codebase without first reading the relevant skill.** The schema has strict invariants (partitioning, compression, lock escalation) that are easy to violate with generic SQL knowledge.

---

## Background Jobs (Export / Import / Reindex)

Bulk operations use the async job queue. To add a new background job type:
1. Use the scaffold tool at `tools/AsyncJobGenerator`.
2. Read the `fhir-sql-background-jobs` skill before touching any SQL.
3. Register the new `QueueType` constant in C# and add a Watchdog coordinator.

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
| `well-architected-agent` | Architecture reviews across reliability/security/performance/cost |

## Available Skills (`.github/skills/`)

Beyond the SQL skills above, these cross-cutting skills are available:

| Skill | Use when |
|-------|---------|
| `engineer-mode` | Need architect-level design trade-off thinking |
| `create-adr` | Drafting an Architectural Decision Record |
| `wa-full-review` | Full Well-Architected Framework review |
| `wa-security-review` | Security-focused review |
| `wa-performance-review` | Performance-focused review |
| `wa-reliability-review` | Reliability-focused review |
