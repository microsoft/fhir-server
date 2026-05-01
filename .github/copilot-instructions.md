# Copilot Instructions for Microsoft FHIR Server

> Core project guidelines, coding standards, and architecture principles are in **[AGENTS.md](../AGENTS.md)** at the repo root — read it first.
> This file adds Copilot-specific skill activation routing and ADR guidance.

---

## Skill Activation

Activate the relevant skill from `.github/skills/` **before** any SQL or architecture task.
The schema has strict invariants; generic SQL knowledge will produce incorrect code.

| Task | Skill |
|------|-------|
| Schema migration / new version | `fhir-sql-schema-migrations` |
| New table or index | `fhir-sql-partitioning-and-indexing` |
| New or modified stored procedure | `fhir-sql-stored-procedure-conventions` |
| Background job / export / import / reindex | `fhir-sql-job-queue-and-watchdog` |
| Resource create/update/delete/history | `fhir-sql-resource-lifecycle` |
| Slow FHIR search or query plan diagnosis | `fhir-sql-performance-diagnostics` |
| Search-to-SQL pipeline, CTE debugging | `fhir-sql-query-generation-pipeline` |
| Hyperscale, read replicas, geo-replication | `fhir-sql-hyperscale-and-operations` |
| Architecture decision (ADR) | `create-adr` + `engineer-mode` |
| Well-Architected review | `wa-full-review` (or focused variant) |

---

## ADR Guidance

When creating or updating an ADR in `docs/arch/`:
- Use the `create-adr` skill.
- Cover states, behaviors, outcomes, and edge cases thoroughly.
- Reference prior ADRs and the FHIR specification.
- If the decision obsoletes a prior ADR, update that ADR to say so.

---

Happy coding!
