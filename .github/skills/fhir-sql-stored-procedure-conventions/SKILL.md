---
name: fhir-sql-stored-procedure-conventions
description: |
  Stored procedure conventions for the Microsoft FHIR Server SQL schema.
  Activate when: creating a stored procedure, modifying a FHIR sproc, adding LogEvent calls,
  error handling in T-SQL, "SET XACT_ABORT", "LogEvent", "@SP", "@Mode", "@DummyTop",
  "MAXDOP 1", "OPTIMIZE FOR", "error_number 1750", "sp_getapplock", THROW pattern,
  trancount preservation, writing a new database procedure for the FHIR server.
---

# FHIR SQL Stored Procedure Conventions

## When to use this skill

Use this skill whenever you need to:
- Create a new stored procedure in the FHIR server SQL schema
- Modify an existing stored procedure
- Add error handling, logging, or query hints
- Understand the @DummyTop or MAXDOP patterns

## Core invariants

1. **Every procedure MUST use `SET NOCOUNT ON`** — prevents row count messages from being sent to the client, reducing network overhead.

2. **Every procedure MUST use `SET XACT_ABORT ON`** when it contains transactions — ensures the entire transaction rolls back on any error.

3. **Every significant procedure MUST log entry and exit** via `dbo.LogEvent` with `@Process=@SP, @Mode=@Mode, @Status='Start'|'End'|'Error'`.

4. **Error handling MUST check `error_number() = 1750`** — this SQL Server error indicates a cascading failure from a child procedure and must be passed through via `THROW` without wrapping.

5. **Transaction count MUST be preserved** — if the caller has an open transaction (`@@trancount > 0`), the procedure must not commit or roll back the caller's transaction. Save `@InitialTranCount = @@trancount` at entry.

6. **Always use `THROW`** (not `RAISERROR`) in CATCH blocks to re-raise errors, preserving the original error context.

## Required patterns

### Standard procedure template
```sql
CREATE OR ALTER PROCEDURE dbo.MyNewProcedure
    @ResourceTypeId smallint
   ,@InputParam varchar(100)
AS
SET NOCOUNT ON
DECLARE @SP varchar(100) = 'MyNewProcedure'
       ,@Mode varchar(200) = 'RT='+convert(varchar,@ResourceTypeId)+' Param='+isnull(@InputParam,'NULL')
       ,@st datetime = getUTCdate()
       ,@Rows bigint = 0
       ,@InitialTranCount int = @@trancount

BEGIN TRY
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start'

  -- === Main logic here ===

  SET @Rows = @@rowcount
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW  -- Pass through cascading errors
  IF @InitialTranCount = 0 AND @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error'
  ;THROW
END CATCH
GO
```

### The @DummyTop pattern (for search/query procedures)
```sql
DECLARE @DummyTop bigint = 9223372036854775807  -- bigint.MaxValue

SELECT TOP (@DummyTop)
    r.ResourceSurrogateId
   ,r.ResourceId
   ,r.Version
   ,r.RawResource
FROM dbo.Resource r
WHERE r.ResourceTypeId = @ResourceTypeId
  AND r.ResourceSurrogateId BETWEEN @StartId AND @EndId
  AND r.IsHistory = 0
  AND r.IsDeleted = 0
OPTION (OPTIMIZE FOR (@DummyTop = 1))
```
**Why**: Forces the optimizer to compile a plan expecting 1 row (encouraging index seeks and nested loops) while returning all rows at runtime. Prevents plan instability from parameter sniffing.

### Application lock pattern (for serialized operations)
```sql
DECLARE @Lock varchar(200) = 'MyOperation_'+convert(varchar,@QueueType)
       ,@LockResult int

EXECUTE @LockResult = sp_getapplock @Resource = @Lock, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 5000
IF @LockResult < 0
BEGIN
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Text='Lock timeout'
  THROW 50001, 'Lock acquisition failed', 1
END
-- Lock auto-released on COMMIT
```

### MAXDOP 1 hint (for serial operations)
```sql
DELETE FROM dbo.TokenSearchParam
WHERE ResourceTypeId = @ResourceTypeId
  AND ResourceSurrogateId = @ResourceSurrogateId
OPTION (MAXDOP 1)
```

## Common mistakes to avoid

- **Using RAISERROR instead of THROW**: RAISERROR doesn't honor XACT_ABORT and doesn't preserve original error context
- **Missing error_number() = 1750 check**: Will swallow cascading errors from child procedures
- **Committing caller's transaction**: If `@InitialTranCount > 0`, the caller owns the transaction — don't commit it
- **Missing LogEvent calls**: Every significant procedure must log Start/End/Error for operational visibility
- **Forgetting SET NOCOUNT ON**: Generates unnecessary row count messages
- **Using OPTIMIZE FOR without @DummyTop**: The pattern requires both the TOP clause and the OPTIMIZE FOR hint to work correctly
- **Adding MAXDOP 1 to long-running queries**: Only appropriate for operations that are inherently serial; long scans benefit from parallelism

## Checklist before committing

- [ ] Procedure uses `CREATE OR ALTER PROCEDURE`
- [ ] `SET NOCOUNT ON` present
- [ ] `SET XACT_ABORT ON` present (if proc has transactions)
- [ ] `@SP`, `@Mode`, `@st`, `@Rows`, `@InitialTranCount` declared
- [ ] `LogEvent` called at Start and End
- [ ] `error_number() = 1750` checked first in CATCH
- [ ] `@InitialTranCount` checked before ROLLBACK
- [ ] `THROW` (not RAISERROR) used in CATCH
- [ ] All queries on partitioned tables include partition key in WHERE
- [ ] @DummyTop pattern used for search queries (if applicable)
- [ ] MAXDOP 1 used only for appropriate serial operations

## Canonical examples

- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Sprocs/MergeResources.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Sprocs/DequeueJob.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Sprocs/LogEvent.sql`
- `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Sprocs/GetResourcesByTypeAndSurrogateIdRange.sql`
