---
mode: 'agent'
description: 'Review the current pull request or diff using the repository PR review workflow adapted from the PR review toolkit'

---

# PR Review

Review the current pull request, branch diff, or explicitly provided range using `.github/instructions/pr-review.instructions.md`.

## Process

1. Determine the review scope.
   - If a PR is available, review the PR diff.
   - If no PR is available, review the current branch diff against the appropriate base branch.
   - If the user supplied files or a range, review only that scope.
2. Inspect changed files before commenting.
3. Apply the review facets from `.github/instructions/pr-review.instructions.md`:
   - General correctness.
   - Tests.
   - Error handling and silent failures.
   - Comments and documentation.
   - Type design.
   - Simplification.
4. Focus on findings that matter for merge readiness.
5. Report findings with file and line references when possible.

## Output format

```markdown
## PR review summary

### Critical
- [file:line] Finding, impact, and suggested fix.

### Important
- [file:line] Finding, impact, and suggested fix.

### Minor
- [file:line] Finding, impact, and suggested fix.

### Positive observations
- Specific strengths in the changed code.

### Validation notes
- Commands reviewed or run. Say "Not run" for anything not executed.
```

If no high-confidence issues are found, say that no merge-blocking issues were found and summarize which review facets were checked.
