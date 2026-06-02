---
name: wa-security-review
description: >
  Conduct a focused security audit based on the Well-Architected Framework Security pillar.
  Use when user says "security review", "wa security", or "security audit".
  Analyzes authentication, authorization, data protection, input validation, and secrets management.
---

# Well-Architected Security Review

Conduct a focused security audit based on the Well-Architected Framework Security pillar.

**Usage**: When user says "security review", "wa security", or "security audit"

## Security Pillar Focus Areas

- Authentication & Authorization (identity management, RBAC)
- Data Protection (encryption at rest/transit, PII handling)
- Input Validation (SQL injection, XSS, command injection prevention)
- Secrets Management (no hardcoded credentials)
- Security Headers & Configuration

## Analysis Targets

Analyze the codebase for:
- 🚨 Critical security vulnerabilities (hardcoded secrets, SQL injection risks)
- ⚠️ Security weaknesses (missing authorization, weak encryption)
- ✅ Security best practices observed

## Output

Provide specific file references and actionable remediation steps.

Use the Well-Architected Agent with security pillar deep dive scope.
