#!/usr/bin/env python3
"""
PR Review Assistant Agent for microsoft/fhir-server.

Fetches the PR diff, classifies changed files by risk level using path-pattern
heuristics, sends the diff to the GitHub Models API for code-quality review and
risk scoring, posts a structured PR review, and auto-approves the PR when all
low-risk gates pass.

The GitHub Models API is free for GitHub/Microsoft users and is authenticated
with the GITHUB_TOKEN that is automatically available in every Actions run —
no extra secrets required.

Required environment variables:
    GITHUB_TOKEN      - GitHub token with pull-requests:write and checks:read
                        (automatically provided by GitHub Actions)
    GITHUB_REPOSITORY - owner/repo (e.g. microsoft/fhir-server)
    PR_NUMBER         - Pull request number
"""

from __future__ import annotations

import fnmatch
import json
import os
import sys
import textwrap
import time
from typing import Any

import requests

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
RISK_RULES_PATH = os.path.join(SCRIPT_DIR, "risk_rules.json")

GITHUB_API = "https://api.github.com"
# GitHub Models API — free for GitHub/Microsoft users, authenticated via GITHUB_TOKEN
GITHUB_MODELS_API = "https://models.inference.ai.azure.com/chat/completions"

SYSTEM_PROMPT = textwrap.dedent("""
    You are a senior engineer reviewing a pull request on the Microsoft FHIR Server (C#/.NET, Azure).
    Write like a colleague doing a real peer review — first-person, direct, no filler.

    Your job in this pass is to identify CANDIDATE issues — things the diff makes you suspicious about,
    but that need to be verified against the full file before flagging. Be specific about what to check.

    For each candidate, give:
    - exactly which file and what pattern/method/property to look for
    - a falsifiable claim: "method X does NOT set Y" or "there is no test for case Z"
    - NOT vague concerns like "make sure X is consistent"

    Return ONLY valid JSON:
    {
      "summary": "<1-2 sentences: what this PR does, plain English>",
      "candidates": [
        {
          "file": "<relative file path>",
          "severity": "error|warning",
          "category": "bug|fhir|testing|performance|security",
          "claim": "<specific falsifiable claim about the code, e.g. 'SyncSearchParametersAsync does not set IsDateOnly'>",
          "check": "<what to grep/look for in the full file to confirm or deny this>"
        }
      ],
      "risk_score": <integer 1-10>,
      "risk_reason": "<one sentence>"
    }

    risk_score: 1-3=low (tests/docs only), 4-6=medium (logic/features), 7-10=high (auth/schema/security).
    Only raise candidates you would actually want to block or flag on this PR.
""").strip()

VERIFY_PROMPT = textwrap.dedent("""
    You are verifying a specific claim about a source file.
    Answer only based on the file content provided — do not speculate.

    Claim: {claim}
    What to look for: {check}

    If the claim is TRUE (the problem exists), respond:
    {{"confirmed": true, "message": "<one sentence describing the actual problem you found, first-person, specific line or method name>"}}

    If the claim is FALSE (the code handles it correctly), respond:
    {{"confirmed": false, "message": ""}}

    Return ONLY valid JSON.
""").strip()


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def load_risk_rules() -> dict[str, Any]:
    with open(RISK_RULES_PATH, encoding="utf-8") as f:
        return json.load(f)


def github_headers(token: str) -> dict[str, str]:
    return {
        "Authorization": f"Bearer {token}",
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
    }


def get_pr_files(owner: str, repo: str, pr_number: int, token: str) -> list[dict]:
    """Return list of changed file objects for a PR (handles pagination)."""
    files: list[dict] = []
    page = 1
    while True:
        url = f"{GITHUB_API}/repos/{owner}/{repo}/pulls/{pr_number}/files"
        resp = requests.get(url, headers=github_headers(token), params={"per_page": 100, "page": page}, timeout=30)
        resp.raise_for_status()
        batch = resp.json()
        if not batch:
            break
        files.extend(batch)
        page += 1
    return files


def get_pr_diff(owner: str, repo: str, pr_number: int, token: str) -> str:
    """Fetch the unified diff of a PR."""
    url = f"{GITHUB_API}/repos/{owner}/{repo}/pulls/{pr_number}"
    headers = {**github_headers(token), "Accept": "application/vnd.github.v3.diff"}
    resp = requests.get(url, headers=headers, timeout=60)
    resp.raise_for_status()
    return resp.text


def get_check_runs(owner: str, repo: str, head_sha: str, token: str) -> list[dict]:
    """Return all check runs for the PR head commit."""
    url = f"{GITHUB_API}/repos/{owner}/{repo}/commits/{head_sha}/check-runs"
    resp = requests.get(url, headers=github_headers(token), params={"per_page": 100}, timeout=30)
    resp.raise_for_status()
    return resp.json().get("check_runs", [])


def get_pr_head_sha(owner: str, repo: str, pr_number: int, token: str) -> str:
    url = f"{GITHUB_API}/repos/{owner}/{repo}/pulls/{pr_number}"
    resp = requests.get(url, headers=github_headers(token), timeout=30)
    resp.raise_for_status()
    return resp.json()["head"]["sha"]


def get_file_content(owner: str, repo: str, path: str, ref: str, token: str) -> str | None:
    """Fetch the full content of a file at a specific ref. Returns None if not found."""
    url = f"{GITHUB_API}/repos/{owner}/{repo}/contents/{path.lstrip('/')}"
    resp = requests.get(url, headers=github_headers(token), params={"ref": ref}, timeout=30)
    if resp.status_code == 404:
        return None
    resp.raise_for_status()
    import base64
    data = resp.json()
    if data.get("encoding") == "base64":
        return base64.b64decode(data["content"]).decode("utf-8", errors="replace")
    return data.get("content", "")


# ---------------------------------------------------------------------------
# Risk classification
# ---------------------------------------------------------------------------

def classify_files(changed_files: list[dict], rules: dict) -> dict[str, list[str]]:
    """
    Classify each changed file into high/medium/low risk.
    A file is classified at the highest matching tier.
    """
    levels = {"high": [], "medium": [], "low": [], "unclassified": []}
    high_patterns = rules["risk_levels"]["high"]["patterns"]
    medium_patterns = rules["risk_levels"]["medium"]["patterns"]
    low_patterns = rules["risk_levels"]["low"]["patterns"]

    for f in changed_files:
        filename = f["filename"]
        if _matches_any(filename, high_patterns):
            levels["high"].append(filename)
        elif _matches_any(filename, medium_patterns):
            levels["medium"].append(filename)
        elif _matches_any(filename, low_patterns):
            levels["low"].append(filename)
        else:
            levels["unclassified"].append(filename)

    return levels


def _matches_any(path: str, patterns: list[str]) -> bool:
    for pattern in patterns:
        if fnmatch.fnmatch(path, pattern) or fnmatch.fnmatch(path.lower(), pattern.lower()):
            return True
    return False


# ---------------------------------------------------------------------------
# AI review — two-pass: analysis → verification
# ---------------------------------------------------------------------------

def _filter_code_diff(diff: str) -> str:
    """Return only src/ and test/ hunks from a unified diff, dropping docs/yml/md."""
    CODE_PREFIXES = ("src/", "test/", "a/src/", "b/src/", "a/test/", "b/test/")
    sections: list[str] = []
    current: list[str] = []
    in_code = False

    for line in diff.splitlines(keepends=True):
        if line.startswith("diff --git"):
            if current and in_code:
                sections.append("".join(current))
            current = [line]
            in_code = any(p in line for p in CODE_PREFIXES)
        else:
            current.append(line)

    if current and in_code:
        sections.append("".join(current))

    return "".join(sections) if sections else diff


def _models_call(messages: list[dict], model: str, temperature: float, github_token: str) -> dict:
    """Single call to GitHub Models API with retry on rate limit."""
    headers = {"Authorization": f"Bearer {github_token}", "Content-Type": "application/json"}
    payload = {
        "model": model,
        "temperature": temperature,
        "response_format": {"type": "json_object"},
        "messages": messages,
    }
    for attempt in range(3):
        resp = requests.post(GITHUB_MODELS_API, headers=headers, json=payload, timeout=120)
        if resp.status_code == 429:
            wait = 20 * (attempt + 1)
            print(f"  Rate limited, waiting {wait}s...")
            time.sleep(wait)
            continue
        resp.raise_for_status()
        return json.loads(resp.json()["choices"][0]["message"]["content"])
    raise RuntimeError("GitHub Models API rate limit exceeded after 3 attempts.")


def _verify_candidate(
    candidate: dict,
    owner: str,
    repo: str,
    head_sha: str,
    model: str,
    temperature: float,
    github_token: str,
) -> dict | None:
    """Fetch the full file and ask the model to confirm or deny the candidate claim.

    Returns a verified suggestion dict if confirmed, None if the claim is false.
    """
    file_path = candidate.get("file", "")
    content = get_file_content(owner, repo, file_path, head_sha, github_token)
    if not content:
        print(f"    Could not fetch {file_path}, skipping verification")
        return None

    # Truncate large files to stay within token budget
    file_snippet = content[:12000]
    if len(content) > 12000:
        file_snippet += "\n\n[... file truncated ...]"

    prompt = VERIFY_PROMPT.format(claim=candidate["claim"], check=candidate["check"])
    messages = [
        {"role": "system", "content": prompt},
        {"role": "user", "content": f"File: {file_path}\n\n```csharp\n{file_snippet}\n```"},
    ]

    result = _models_call(messages, model, temperature, github_token)
    if result.get("confirmed"):
        return {
            "file": file_path,
            "line": None,
            "severity": candidate.get("severity", "warning"),
            "category": candidate.get("category", "bug"),
            "message": result.get("message", candidate["claim"]),
        }
    return None


def call_github_models(
    diff: str,
    owner: str,
    repo: str,
    head_sha: str,
    rules: dict,
    github_token: str,
) -> dict[str, Any]:
    """Two-pass review: (1) identify candidates from diff, (2) verify each against the full file."""
    max_chars = rules["ai"]["max_diff_chars"]
    model = rules["ai"]["model"]
    temperature = rules["ai"]["temperature"]

    code_diff = _filter_code_diff(diff)
    truncated_diff = code_diff[:max_chars]
    if len(code_diff) > max_chars:
        truncated_diff += f"\n\n[... diff truncated at {max_chars} characters ...]"

    # Pass 1: analysis — produce candidate issues with falsifiable claims
    print("  Pass 1: analyzing diff for candidate issues...")
    analysis = _models_call(
        [
            {"role": "system", "content": SYSTEM_PROMPT},
            {"role": "user", "content": f"Review this pull request diff:\n\n```diff\n{truncated_diff}\n```"},
        ],
        model,
        temperature,
        github_token,
    )

    candidates = analysis.get("candidates", [])
    print(f"  Pass 1 complete: {len(candidates)} candidate(s) to verify")

    # Pass 2: verify each candidate against the full file
    verified_suggestions: list[dict] = []
    for i, candidate in enumerate(candidates):
        file_path = candidate.get("file", "")
        print(f"  Verifying [{i+1}/{len(candidates)}]: {file_path} — {candidate.get('claim', '')[:80]}")
        result = _verify_candidate(candidate, owner, repo, head_sha, model, temperature, github_token)
        if result:
            print(f"    ✓ Confirmed")
            verified_suggestions.append(result)
        else:
            print(f"    ✗ Not confirmed — dropped")

    print(f"  Pass 2 complete: {len(verified_suggestions)}/{len(candidates)} issue(s) confirmed")

    return {
        "summary": analysis.get("summary", ""),
        "suggestions": verified_suggestions,
        "risk_score": analysis.get("risk_score", 5),
        "risk_reason": analysis.get("risk_reason", ""),
    }




# ---------------------------------------------------------------------------
# GitHub PR review posting
# ---------------------------------------------------------------------------

def post_pr_review(
    owner: str,
    repo: str,
    pr_number: int,
    token: str,
    ai_review: dict,
    risk_levels: dict[str, list[str]],
    auto_approve: bool,
    auto_approve_reason: str,
) -> None:
    """Post a structured review comment on the PR."""
    body = _build_review_body(ai_review, risk_levels, auto_approve, auto_approve_reason)
    event = "APPROVE" if auto_approve else _decide_review_event(ai_review)

    url = f"{GITHUB_API}/repos/{owner}/{repo}/pulls/{pr_number}/reviews"
    payload = {"body": body, "event": event}

    # Attach inline comments for suggestions with a specific file+line
    inline_comments = []
    for suggestion in ai_review.get("suggestions", []):
        if suggestion.get("file") and suggestion.get("file") != "general" and suggestion.get("line"):
            inline_comments.append({
                "path": suggestion["file"],
                "position": 1,  # We use body-level for safety; inline requires diff position mapping
                "body": f"**[{suggestion['severity'].upper()}]** ({suggestion['category']}): {suggestion['message']}",
            })

    # GitHub only supports inline comments at exact diff positions, which requires
    # diff position mapping. We include all feedback in the top-level body instead.
    resp = requests.post(url, headers=github_headers(token), json=payload, timeout=30)
    resp.raise_for_status()
    print(f"Posted review with event={event}, id={resp.json()['id']}")


def _decide_review_event(ai_review: dict) -> str:
    """Return COMMENT or REQUEST_CHANGES based on error-severity suggestions."""
    has_errors = any(s.get("severity") == "error" for s in ai_review.get("suggestions", []))
    return "REQUEST_CHANGES" if has_errors else "COMMENT"


def _build_review_body(
    ai_review: dict,
    risk_levels: dict[str, list[str]],
    auto_approve: bool,
    auto_approve_reason: str,
) -> str:
    risk_score = ai_review.get("risk_score", "N/A")
    risk_reason = ai_review.get("risk_reason", "")
    summary = ai_review.get("summary", "")
    suggestions = ai_review.get("suggestions", [])

    if isinstance(risk_score, int):
        badge = "🟢" if risk_score <= 3 else ("🟡" if risk_score <= 6 else "🔴")
    else:
        badge = "⚪"

    lines = [f"## Review {badge} {risk_score}/10", "", summary, ""]

    if risk_reason:
        lines += [f"> {risk_reason}", ""]

    if risk_levels["high"]:
        lines.append("**High-risk files:** " + ", ".join(f"`{f}`" for f in risk_levels["high"]))
        lines.append("")

    errors = [s for s in suggestions if s.get("severity") == "error"]
    warnings = [s for s in suggestions if s.get("severity") == "warning"]

    if errors or warnings:
        for s in errors + warnings:
            prefix = "❌" if s.get("severity") == "error" else "⚠️"
            loc = f"`{s['file']}`" if s.get("file") and s["file"] != "general" else ""
            loc += f" (line {s['line']})" if s.get("line") else ""
            loc_str = f" — {loc}" if loc else ""
            lines.append(f"{prefix}{loc_str} {s['message']}")
        lines.append("")
    else:
        lines += ["Looks good — no issues found.", ""]

    if auto_approve:
        lines.append("✅ Auto-approved — low risk, all checks passed.")
    else:
        lines.append(f"*Not auto-approved: {auto_approve_reason}*")

    return "\n".join(lines)


# ---------------------------------------------------------------------------
# Auto-approve logic
# ---------------------------------------------------------------------------

def evaluate_auto_approve(
    risk_levels: dict[str, list[str]],
    ai_risk_score: int,
    check_runs: list[dict],
    rules: dict,
) -> tuple[bool, str]:
    """Return (should_approve, reason_string)."""
    max_score = rules["auto_approve"]["max_risk_score"]

    if risk_levels["high"]:
        return False, f"{len(risk_levels['high'])} high-risk file(s) changed (requires human review)"

    if ai_risk_score > max_score:
        return False, f"AI risk score {ai_risk_score}/10 exceeds threshold of {max_score}"

    failing = [r for r in check_runs if r.get("conclusion") in ("failure", "timed_out", "cancelled") and r.get("name") != "PR Review Assistant"]
    if failing:
        names = ", ".join(r["name"] for r in failing[:3])
        return False, f"Failing CI checks: {names}"

    return True, (
        f"no high-risk files changed, AI risk score {ai_risk_score}/10 ≤ {max_score}, "
        f"and all CI checks passed"
    )


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main() -> None:
    github_token = os.environ.get("GITHUB_TOKEN", "")
    repo_full = os.environ.get("GITHUB_REPOSITORY", "")
    pr_number_str = os.environ.get("PR_NUMBER", "")

    missing = [k for k, v in {
        "GITHUB_TOKEN": github_token,
        "GITHUB_REPOSITORY": repo_full,
        "PR_NUMBER": pr_number_str,
    }.items() if not v]

    if missing:
        print(f"ERROR: Missing required environment variables: {', '.join(missing)}", file=sys.stderr)
        sys.exit(1)

    owner, repo = repo_full.split("/", 1)
    pr_number = int(pr_number_str)

    print(f"Reviewing PR #{pr_number} in {repo_full}...")

    rules = load_risk_rules()

    # Fetch PR data
    print("Fetching PR files...")
    changed_files = get_pr_files(owner, repo, pr_number, github_token)
    print(f"  {len(changed_files)} file(s) changed")

    print("Fetching PR diff...")
    diff = get_pr_diff(owner, repo, pr_number, github_token)
    print(f"  Diff size: {len(diff)} chars")

    print("Fetching head SHA and check runs...")
    head_sha = get_pr_head_sha(owner, repo, pr_number, github_token)
    check_runs = get_check_runs(owner, repo, head_sha, github_token)
    print(f"  {len(check_runs)} check run(s) found")

    # Classify files by risk
    risk_levels = classify_files(changed_files, rules)
    print(f"  Risk: high={len(risk_levels['high'])}, medium={len(risk_levels['medium'])}, low={len(risk_levels['low'])}, unclassified={len(risk_levels['unclassified'])}")

    # AI review via GitHub Models (free, uses GITHUB_TOKEN) — two-pass with verification
    print("Calling GitHub Models API for code review...")
    ai_review = call_github_models(diff, owner, repo, head_sha, rules, github_token)
    risk_score = ai_review.get("risk_score", 10)
    print(f"  AI risk score: {risk_score}/10")
    print(f"  Suggestions: {len(ai_review.get('suggestions', []))}")

    # Auto-approve decision
    should_approve, approve_reason = evaluate_auto_approve(risk_levels, risk_score, check_runs, rules)
    print(f"  Auto-approve: {should_approve} — {approve_reason}")

    # Post review
    print("Posting PR review...")
    post_pr_review(owner, repo, pr_number, github_token, ai_review, risk_levels, should_approve, approve_reason)

    print("Done!")


if __name__ == "__main__":
    main()
