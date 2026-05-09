# Copilot Instructions for Microsoft FHIR Server

> Core project guidelines, coding standards, and architecture principles are in **[AGENTS.md](../AGENTS.md)** at the repo root — read it first.
> This file adds Copilot-specific skill activation routing and ADR guidance.

---

## Skill Activation

Activate the relevant skill from `.github/skills/` **before** any SQL or architecture task.
The schema has strict invariants; generic SQL knowledge will produce incorrect code.

| Task | Skill |
|------|-------|
| Schema migration / new version | `fhir-sql-author-schema-migration` |
| New table or index | `fhir-sql-add-table-or-index` |
| New or modified stored procedure | `fhir-sql-write-stored-procedure` |
| Background job / export / import / reindex | `fhir-sql-background-jobs` |
| Resource create/update/delete/history | `fhir-sql-resource-crud-and-history` |
| Slow FHIR search or query plan diagnosis | `fhir-sql-diagnose-query-perf` |
| Search-to-SQL pipeline, CTE debugging | `fhir-sql-query-generation-pipeline` |
| Hyperscale, read replicas, geo-replication | `fhir-sql-production-operations` |
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
