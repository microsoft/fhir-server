# PR Review Instructions

For PR reviews, use `code-review-agent` as the high-level entry agent. It owns subagent selection, fan-out, fan-in, and final reporting.

When a focused review is requested, keep the same output format while applying only the requested focus.

Required output sections:
- Critical
- Important
- Minor
- Positive observations
- Validation notes

## Review mode

- Report findings only against the provided changed files or scope summary, but inspect surrounding and related repository context as needed.
- Keep the required output sections unchanged.
