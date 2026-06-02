---
name: wa-performance-review
description: >
  Conduct a focused performance analysis based on the Well-Architected Framework Performance Efficiency pillar.
  Use when user says "performance review", "wa performance", or "check performance".
  Analyzes scalability, data access, caching, algorithm efficiency, network I/O, and resource management.
---

# Well-Architected Performance Review

Conduct a focused performance analysis based on the Well-Architected Framework Performance Efficiency pillar.

**Usage**: When user says "performance review", "wa performance", or "check performance"

## Performance Pillar Focus Areas

- Scalability (horizontal scaling, async operations)
- Data Access (N+1 queries, connection pooling, indexing)
- Caching Strategy (memory/distributed caching, invalidation)
- Algorithm Efficiency (time/space complexity, Query optimization)
- Network & I/O (batching, compression, streaming)
- Resource Management (memory leaks, GC pressure, thread pool)

## Analysis Targets

Analyze the codebase for:
- 🚨 Critical performance issues (blocking calls, N+1 queries)
- ⚠️ Performance bottlenecks (inefficient algorithms, missing caching)
- ✅ Performance best practices observed

## Output

Provide specific file references with optimization recommendations and effort estimates.

Use the Well-Architected Agent with performance pillar deep dive scope.
