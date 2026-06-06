# Code Review Agents

This folder contains the GitHub Copilot custom agents used for code and PR review requests.

## Entry point

Use `code-review-agent` for all user-facing review requests. It is the high-level review entry agent. It collects shared context, decides which specialized agents apply, fans out to them, and synthesizes the final report.

## Specialized agents

Specialized agents should be narrow. Each agent owns one review concern and returns either standard findings or `No findings`.

Current specialized agents:

- `code-review-correctness-agent`
- `code-review-tests-agent`
- `code-review-silent-failures-agent`
- `code-review-type-design-agent`
- `code-review-comments-docs-agent`
- `code-review-simplification-agent`

## Adding a new review agent

1. Create `.github/agents/code-review-<name>-agent.agent.md`.
2. Use frontmatter with `name`, `description`, `model`, and only the tools the agent needs.
3. Keep the agent focused on one review concern.
4. Add the agent to the `Subagent selection` table in `code-review-agent.agent.md`.
5. Add selection triggers that let the top-level agent decide when the new agent should run.
6. Update this README with the new agent and any important routing guidance.

### Example: future pipeline config agent

A future `code-review-pipeline-config-agent` should run only when GitHub Actions, Azure DevOps pipelines, build/release automation, or CI/CD configuration files change. It should not run for ordinary source-only PRs. Its selection triggers would likely include `.github/workflows/**`, `.azuredevops/**`, `azure-pipelines*.yml`, and `azure-pipelines*.yaml`.

Do not duplicate synthesis rules in specialized agents. The top-level agent is responsible for orchestration, deduplication, and final reporting.
