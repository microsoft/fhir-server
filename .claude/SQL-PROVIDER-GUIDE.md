# SQL Data Provider Agent Guide

## Quick Start

The SQL Provider Agent is a specialized expert for working with the FHIR Server's SQL data provider architecture. Use it when working on:

- Expression tree analysis
- Rewriter pipeline changes
- Query generation modifications
- Schema version migrations
- Search performance optimization
- Debugging SQL-related issues

## How to Use

### Option 1: Direct Agent Invocation
```
@sql-provider-agent <your question or task>
```

### Option 2: Via Task Tool
When Claude Code suggests using a task agent, you can specify the sql-provider-agent.

## Example Use Cases

### 1. Understanding Expression Flow
```
@sql-provider-agent Explain how the FHIR query "Patient?name=Smith&birthdate=gt2000-01-01"
becomes a SQL query. Show the expression tree, rewriter transformations, and final SQL.
```

### 2. Debugging Search Issues
```
@sql-provider-agent I'm getting slow queries for chained searches like
"Observation?subject.name=Smith". Help me understand the expression tree,
rewriter pipeline, and generated SQL. What optimizations am I missing?
```

### 3. Adding New Search Parameter Support
```
@sql-provider-agent I need to add support for a new composite search parameter
"token-reference-token". Which files do I need to modify? What's the pattern?
```

### 4. Schema Version Guidance
```
@sql-provider-agent I want to add a new optimization that requires a schema change.
What's the process? Which version constants do I need to update?
```

### 5. Performance Optimization
```
@sql-provider-agent This search query generates inefficient SQL with multiple
table scans. Analyze the rewriter pipeline and suggest optimizations.
```

### 6. Rewriter Pipeline Analysis
```
@sql-provider-agent Can you validate the rewriter execution order in SqlServerSearchService?
I'm adding a new rewriter - where should it go in the pipeline?
```

## Architecture Layers Quick Reference

### Layer 1: Core Expressions
- **Location**: `Microsoft.Health.Fhir.Core/Features/Search/Expressions/`
- **Purpose**: Abstract expression trees representing FHIR queries
- **Key Files**: Expression.cs, IExpressionVisitor.cs, ExpressionParser.cs

### Layer 2: SQL Extensions
- **Location**: `Microsoft.Health.Fhir.SqlServer/Features/Search/Expressions/`
- **Purpose**: SQL-specific expression types
- **Key Files**: SqlRootExpression.cs, SearchParamTableExpression.cs, ISqlExpressionVisitor.cs

### Layer 3: Rewriters (24 files)
- **Location**: `Microsoft.Health.Fhir.SqlServer/Features/Search/Expressions/Visitors/`
- **Purpose**: Transform and optimize expression trees
- **Critical**: Order matters! Defined in SqlServerSearchService.cs

### Layer 4: Query Generators (29 files)
- **Location**: `Microsoft.Health.Fhir.SqlServer/Features/Search/Expressions/Visitors/QueryGenerators/`
- **Purpose**: Convert expressions to T-SQL
- **Key Files**: SqlQueryGenerator.cs (1,565 lines), SearchParamTableExpressionQueryGeneratorFactory.cs

### Layer 5: Schema (96 versions)
- **Location**: `Microsoft.Health.Fhir.SqlServer/Features/Schema/`
- **Purpose**: Database schema management and versioning
- **Key Files**: SchemaVersion.cs, SchemaVersionConstants.cs, Migrations/*.diff.sql

## Common Questions

### Q: How do I trace a search query from HTTP to SQL?
**A**: Ask the agent to explain the complete flow with specific query parameters.

### Q: Which rewriter should I modify for my optimization?
**A**: Describe the optimization to the agent; it will identify the right rewriter or suggest creating a new one.

### Q: What schema version do I need for a new feature?
**A**: Ask the agent about schema version requirements; it knows all 96 versions and their features.

### Q: Why is my generated SQL inefficient?
**A**: Provide the FHIR query and ask the agent to analyze the expression tree, rewriters, and generated SQL.

### Q: How do I add a new query generator?
**A**: Ask the agent for the pattern; it will reference similar generators and show the factory registration.

## Agent Capabilities

The SQL Provider Agent can:

✅ Visualize expression tree transformations
✅ Explain rewriter pipeline execution order
✅ Trace SQL generation step-by-step
✅ Identify schema version requirements
✅ Suggest performance optimizations
✅ Guide debugging with specific file references
✅ Explain design patterns (Visitor, Factory, Builder)
✅ Validate architectural changes
✅ Provide code examples with context

## Tips for Best Results

1. **Be specific**: Include actual FHIR query parameters or code snippets
2. **Ask for visualization**: Request expression tree diagrams or transformation flows
3. **Request file references**: Ask for specific files and line numbers
4. **Seek explanations**: Don't just ask "how" - ask "why" for deeper insights
5. **Test scenarios**: Provide example queries for the agent to analyze

## Integration with CLAUDE.md

The agent has full knowledge of the SQL Provider Deep Dive section in CLAUDE.md. Reference that documentation alongside the agent for comprehensive understanding.

## Advanced Usage

### Debugging Complex Issues
```
@sql-provider-agent I'm seeing incorrect results for NOT queries with chained references.
Walk me through: 1) Expression tree, 2) NotExpressionRewriter behavior,
3) ChainFlatteningRewriter interaction, 4) Generated SQL, 5) Root cause analysis
```

### Schema Migration Planning
```
@sql-provider-agent I need to migrate from V94 to V96. What features are affected?
What stored procedures changed? Show me the migration strategy.
```

### Performance Deep Dive
```
@sql-provider-agent Analyze why "Patient?_has:Observation:subject:code=12345" is slow.
Show predicate pushdown opportunities, rewriter optimizations, and index recommendations.
```

## Getting Started Checklist

- [ ] Read the SQL Provider Deep Dive in CLAUDE.md
- [ ] Try the agent with a simple query trace request
- [ ] Ask the agent to explain the rewriter pipeline
- [ ] Request a schema version compatibility analysis
- [ ] Use the agent to debug a search performance issue

---

**Remember**: The SQL Provider Agent reduces weeks of learning to hours. Use it liberally when working with the SQL data provider!
