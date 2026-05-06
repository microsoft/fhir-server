# PR Review Assistant Agent

An automated GitHub Actions agent that reviews pull requests using AI-powered code analysis, classifies risk levels based on file-path heuristics, and auto-approves low-risk PRs.

## How It Works

```
PR opened/updated
       │
       ▼
Fetch PR diff + changed files
       │
       ▼
Classify files by risk level
(high / medium / low heuristics)
       │
       ▼
Send diff to OpenAI GPT-4o
(FHIR-server-aware system prompt)
       │
       ▼
AI returns: summary + suggestions + risk_score
       │
       ├─ risk_score ≤ 3 AND no high-risk files AND no failing CI
       │         │
       │         ▼
       │    ✅ APPROVE + post review comment
       │
       └─ otherwise
                 │
                 ▼
            🔍 COMMENT or REQUEST_CHANGES + post review
```

## Setup

The agent uses the **GitHub Models API** — free for GitHub/Microsoft users. It authenticates using the `GITHUB_TOKEN` that is **automatically provided** by every GitHub Actions run.

**No secrets or API keys need to be added.** The agent works out of the box.

### The Workflow

The agent runs automatically via `.github/workflows/pr-review-agent.yml` when a PR is:
- Opened
- Updated (new commits pushed)
- Reopened

Bot-created PRs (Dependabot) are excluded to prevent feedback loops.

### Required Permissions

The workflow uses `pull-requests: write` to post reviews and `checks: read` to verify CI status before auto-approving. These are declared in the workflow and no repository-level changes are needed.

---

## Risk Classification

Files are classified at the **highest matching tier**. All patterns use shell glob syntax.

### 🔴 High Risk (blocks auto-approve)

Changes to these paths require mandatory human review:

| Pattern | Reason |
|---------|--------|
| `**/auth*`, `**/Authorization*`, `**/Authentication*` | Authentication & authorization |
| `**/Security*`, `**/Encryption*`, `**/Certificate*` | Security & cryptography |
| `**/Token*`, `**/Secret*` | Secrets & tokens |
| `**/Schema/Migrations/**`, `**/*.sql` | Database schema changes |
| `**/SchemaVersion*`, `**/SchemaVersionConstants*` | Schema versioning |
| `**/Middleware/**`, `**/Filters/**` | Request pipeline |
| `**/appsettings*.json`, `**/Startup.cs`, `**/Program.cs` | App configuration |
| `**/.github/workflows/**` | CI/CD pipeline changes |
| `**/CODEOWNERS`, `**/CredScanSuppressions.json` | Security overrides |

### 🟡 Medium Risk

Changes reviewed carefully but don't block auto-approve:

| Pattern | Reason |
|---------|--------|
| `**/Controllers/**` | API surface changes |
| `**/Core/**`, `**/Api/**` | Business logic |
| `**/CosmosDb/**`, `**/SqlServer/**` | Data access |
| `**/Handlers/**`, `**/Services/**` | Feature logic |
| `**/*.csproj`, `**/Directory.*.props` | Dependency changes |

### 🟢 Low Risk

| Pattern | Examples |
|---------|---------|
| `**/UnitTests/**`, `**/*.Tests/**` | Unit test changes |
| `**/Tests.E2E/**` | E2E test changes |
| `**/*.md`, `docs/**` | Documentation |

---

## Auto-Approve Conditions

A PR is automatically approved **only when ALL three gates pass**:

1. ✅ **No high-risk files** — zero changed files match a high-risk pattern
2. ✅ **AI risk score ≤ 3** — OpenAI rates the change as low-risk (out of 10)
3. ✅ **No failing CI checks** — no check run has a `failure`, `timed_out`, or `cancelled` conclusion

---

## Review Output

The agent posts a structured review comment including:

- **Summary** — AI-generated description of the changes
- **Risk Assessment** — 🟢/🟡/🔴 badge with score and reasoning
- **High/Medium-risk files** — listed explicitly if present
- **Errors** (❌) — must-fix issues (triggers `REQUEST_CHANGES`)
- **Warnings** (⚠️) — should-fix issues
- **Suggestions** (💡) — informational improvements
- **Auto-approve status** — approved or reason why not

---

## Customizing Risk Rules

Edit `.github/scripts/risk_rules.json` to adjust:

```json
{
  "risk_levels": {
    "high": { "patterns": ["**/YourSensitivePath/**"] },
    "medium": { "patterns": [...] },
    "low": { "patterns": [...] }
  },
  "auto_approve": {
    "max_risk_score": 3
  },
  "ai": {
    "model": "gpt-4o",
    "max_diff_chars": 24000
  }
}
```

- Increase `max_risk_score` to be more permissive (max 10)
- Add paths to `high.patterns` to protect additional sensitive areas
- Switch `model` to `gpt-4o` for higher-quality reviews (uses more GitHub Models quota)

---

## Files

| File | Purpose |
|------|---------|
| `.github/workflows/pr-review-agent.yml` | GitHub Actions workflow definition |
| `.github/scripts/pr_review_agent.py` | Agent logic (diff fetch, risk check, AI call, review post) |
| `.github/scripts/risk_rules.json` | Configurable path patterns and thresholds |
| `docs/pr-review-agent.md` | This documentation |
