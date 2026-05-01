---
name: fhir-sql-schema-migrations
description: |
  How to author a new .diff.sql migration file for the Microsoft FHIR Server SQL schema.
  Activate when: creating a schema migration, bumping SchemaVersion, writing a diff.sql file,
  adding a column to a FHIR table, creating a new FHIR stored procedure, modifying database
  DDL in the FHIR server, "new schema version", "migration script", "diff.sql", "SchemaVersionConstants",
  forward compatibility, backward compatibility, rolling upgrade, idempotent migration.
---

# FHIR SQL Schema Migrations

## When to use this skill

Use this skill whenever you need to:
- Create a new `{N}.diff.sql` migration file
- Bump `SchemaVersionConstants.Max` (and potentially `.Min`)
- Add, alter, or drop tables, columns, indexes, or stored procedures
- Ensure forward/backward compatibility during rolling upgrades

## Core invariants

1. **Every DDL change MUST go in a numbered `.diff.sql` file** in `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Migrations/`. The version number is the next integer after the current `SchemaVersionConstants.Max`.

2. **Every diff.sql MUST be idempotent** — running it twice produces no errors and no duplicate changes. Use these patterns:
   ```sql
   -- Adding a column
   IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.TableName') AND name = 'NewColumn')
     ALTER TABLE dbo.TableName ADD NewColumn datatype NULL

   -- Adding an index
   IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_NewIndex' AND object_id = OBJECT_ID('dbo.TableName'))
     CREATE INDEX IX_NewIndex ON dbo.TableName(Col1, Col2) WITH (ONLINE = ON)

   -- Stored procedures
   -- Always use CREATE OR ALTER (inherently idempotent)
   CREATE OR ALTER PROCEDURE dbo.NewProcedure ...
   ```

3. **SchemaVersionConstants.cs MUST be updated** in the same PR:
   ```csharp
   public const int Max = N;  // Increment by 1
   // Min stays the same unless dropping backward compat
   ```

4. **The full cumulative .sql file MUST also be updated** — `{N}.sql` contains the complete schema at version N, used for fresh installs.

5. **Backward compatibility**: The new schema version MUST work with the previous application version (N-1). This means:
   - New columns MUST be nullable (or have defaults)
   - New procedures MUST NOT replace old procedure signatures that N-1 code calls
   - Old procedure versions MUST be kept until `Min` advances past them
   - The expand-contract pattern: Phase 1 (add nullable column) → Phase 2 (application writes to it) → Phase 3 (make NOT NULL in a future version)

6. **Forward compatibility**: The previous schema version (N-1) MUST still work with the new application version (N). The application code must handle both schema versions gracefully.

## Required patterns

```sql
-- File: {N}.diff.sql
-- Every diff file should start with a version check guard
IF NOT EXISTS (SELECT * FROM dbo.SchemaVersion WHERE Version = N-1 AND Status = 'completed')
  -- Previous version must be applied
  RAISERROR('Previous schema version not applied', 18, 127)

-- DDL changes with idempotency guards
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Resource') AND name = 'NewColumn')
BEGIN
  ALTER TABLE dbo.Resource ADD NewColumn varchar(64) NULL
END
GO

-- Index creation with ONLINE = ON
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Resource_NewColumn')
BEGIN
  CREATE NONCLUSTERED INDEX IX_Resource_NewColumn
  ON dbo.Resource(ResourceTypeId, NewColumn)
  WITH (ONLINE = ON, DATA_COMPRESSION = PAGE)
  ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END
GO

-- Stored procedure changes (always CREATE OR ALTER)
GO
CREATE OR ALTER PROCEDURE dbo.ExistingProcedure
  @Param1 int
  ...
AS
SET NOCOUNT ON
-- ... procedure body
GO
```

## Common mistakes to avoid

- **Forgetting idempotency guards**: Every `ALTER TABLE`, `CREATE INDEX` must be wrapped in `IF NOT EXISTS`
- **Adding NOT NULL columns without defaults**: Breaks existing data; always add as nullable first
- **Removing old procedure versions prematurely**: Other instances may still be running the old application version
- **Forgetting ONLINE = ON for index operations**: Blocks production traffic
- **Forgetting partition alignment**: New indexes on partitioned tables MUST specify `ON PartitionScheme_ResourceTypeId(ResourceTypeId)`
- **Forgetting DATA_COMPRESSION = PAGE**: All indexes should use page compression
- **Not updating both .diff.sql AND .sql files**: Fresh installs use the full .sql file

## Checklist before committing

- [ ] `.diff.sql` file is idempotent (run twice without error)
- [ ] `SchemaVersionConstants.Max` incremented
- [ ] Full `.sql` file updated to include all changes
- [ ] Backward compatibility verified (N-1 app works with N schema)
- [ ] Forward compatibility verified (N app works with N-1 schema)
- [ ] All DDL uses `IF NOT EXISTS` guards
- [ ] All index operations use `WITH (ONLINE = ON)`
- [ ] All new indexes on partitioned tables specify partition scheme
- [ ] All new indexes use `DATA_COMPRESSION = PAGE`

## Canonical examples

- Migration files: `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Migrations/`
- Version constants: `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/SchemaVersionConstants.cs`
- Schema docs: `docs/schema-manager.md`, `docs/SchemaMigrationGuide.md`
