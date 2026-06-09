You are publishing an already-completed review for pull request #{{prNumber}} in {{repository}}.

Read the saved report at `{{reportPath}}`. The canonical artifact file is `report.md`.

Requester focus:
{{userContext}}

Requirements:
1. Post one top-level PR comment that summarizes the review.
2. Add inline comments only for findings you can confidently anchor to changed diff lines on head SHA `{{headSha}}`.
3. If a finding cannot be safely anchored, keep it only in the top-level comment.
4. Do not invent findings that are not present in the saved report.
5. If the saved report says there are no high-confidence issues, post only the top-level summary comment.
