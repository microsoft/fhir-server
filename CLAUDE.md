# CLAUDE.md — Microsoft FHIR Server

> Core project guidelines, coding conventions, and architectural principles are in **[AGENTS.md](./AGENTS.md)** — read it first.
> This file adds Claude-specific skill activation instructions.

---

## How to Use Project Skills

Skills live in `.github/skills/<skill-name>/SKILL.md`. Claude cannot discover them automatically — use the `Read` tool to load the relevant skill **before** starting any architecture task.

**Example:** `Read(".github/skills/create-adr/SKILL.md")`

---

## Skill Activation Routing

| Task | Skill path |
|------|-----------|
| Architecture decision (ADR) | `.github/skills/create-adr/SKILL.md` |

---

Happy coding!
