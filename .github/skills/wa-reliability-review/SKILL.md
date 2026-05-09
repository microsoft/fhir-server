---
name: wa-reliability-review
description: >
  Conduct a focused reliability assessment based on the Well-Architected Framework Reliability pillar.
  Use when user says "reliability review", "wa reliability", or "check reliability".
  Analyzes resilience patterns, data reliability, infrastructure resilience, and recovery capabilities.
---

# Well-Architected Reliability Review

Conduct a focused reliability assessment based on the Well-Architected Framework Reliability pillar.

**Usage**: When user says "reliability review", "wa reliability", or "check reliability"

## Reliability Pillar Focus Areas

- Resilience Patterns (error handling, retry logic, circuit breakers)
- Data Reliability (transaction management, validation, consistency)
- Infrastructure Resilience (health checks, dependency isolation, resource limits)
- Recovery Capabilities (graceful degradation, idempotency)

## Analysis Targets

Analyze the codebase for:
- 🚨 Critical reliability gaps (missing error handling, no retry logic)
- ⚠️ Resilience weaknesses (single points of failure, tight coupling)
- ✅ Reliability best practices observed

## Output

Provide specific file references with recommendations for improving system resilience and availability.

Use the Well-Architected Agent with reliability pillar deep dive scope.
