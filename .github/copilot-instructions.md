# Copilot Instructions for Microsoft FHIR Server

> Core project guidelines, coding standards, and architecture principles are in **[AGENTS.md](../AGENTS.md)** at the repo root — read it first.
> This file adds Copilot-specific skill activation routing and ADR guidance.

---

## Skill Activation

Activate the relevant skill from `.github/skills/` **before** any architecture task.

| Task | Skill |
|------|-------|
| Architecture decision (ADR) | `create-adr` |

---

## ADR Guidance

When creating or updating an ADR in `docs/arch/`:
- Use the `create-adr` skill.
- Cover states, behaviors, outcomes, and edge cases thoroughly.
- Reference prior ADRs and the FHIR specification.
- If the decision obsoletes a prior ADR, update that ADR to say so.

---

## Code Review

When reviewing a pull request, use [REVIEW.md](../REVIEW.md) and the applicable [FHIR security review guidelines](instructions/fhir-security.instructions.md). `AGENTS.md` remains authoritative; these documents add review criteria, not new runtime policy. See [review verification](REVIEWING.md) for instruction-loading evidence and maintainer checks.

---

Happy coding!
