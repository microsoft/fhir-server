# CLAUDE.md — Microsoft FHIR Server

> Core project guidelines, coding conventions, and architectural principles are in **[AGENTS.md](./AGENTS.md)** — read it first.
> This file adds Claude-specific skill activation instructions.

---

## How to Use Project Skills

Skills live in `.github/skills/<skill-name>/SKILL.md`. Claude cannot discover them automatically — use the `Read` tool to load the relevant skill **before** starting any SQL or architecture task.

**Example:** `Read(".github/skills/fhir-sql-diagnose-query-perf/SKILL.md")`

---

## Skill Activation Routing

| Task | Skill path |
|------|-----------|
| Schema migration / new version | `.github/skills/fhir-sql-author-schema-migration/SKILL.md` |
| New table or index | `.github/skills/fhir-sql-add-table-or-index/SKILL.md` |
| New or modified stored procedure | `.github/skills/fhir-sql-write-stored-procedure/SKILL.md` |
| Background job / export / import / reindex | `.github/skills/fhir-sql-background-jobs/SKILL.md` |
| Resource create/update/delete/history | `.github/skills/fhir-sql-resource-crud-and-history/SKILL.md` |
| Slow FHIR search or query plan diagnosis | `.github/skills/fhir-sql-diagnose-query-perf/SKILL.md` |
| Search-to-SQL pipeline, CTE debugging | `.github/skills/fhir-sql-query-generation-pipeline/SKILL.md` |
| Hyperscale, read replicas, geo-replication | `.github/skills/fhir-sql-production-operations/SKILL.md` |
| Architecture decision (ADR) | `.github/skills/create-adr/SKILL.md` + `.github/skills/engineer-mode/SKILL.md` |
| Well-Architected review | `.github/skills/wa-full-review/SKILL.md` (or focused variant below) |
| — Security focus | `.github/skills/wa-security-review/SKILL.md` |
| — Performance focus | `.github/skills/wa-performance-review/SKILL.md` |
| — Reliability focus | `.github/skills/wa-reliability-review/SKILL.md` |

**Never write SQL for this codebase without first reading the relevant skill.** The schema has strict invariants (partitioning, compression, lock escalation) that generic SQL knowledge will violate.

---

Happy coding!
