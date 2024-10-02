# Sql development guidelines

## Learning resources

- [SQL server Internals	Microsoft SQL Server 2012 Internals](https://learning.oreilly.com/library/view/microsoft-sql-server/9780735670174/)
- [Managing SQL Server Performance](https://app.pluralsight.com/library/courses/managing-sql-server-database-performance/table-of-contents)

## Style Guide

https://github.com/ktaranov/sqlserver-kit/blob/master/SQL%20Server%20Name%20Convention%20and%20T-SQL%20Programming%20Style.md

## Making changes

### Existing sql file
- Edit the changes in the existing sql file
- Updating Stored proc - Ensure to follow correct SQL formatting

### New table/sproc
- Add a new sql file in the corresponding folder in `Sql` folder
- Update csproj with new `SqlScript` or `TSqlScript` file element

### Common
- Update TransactionCheckWithInitialiScript file with LatestSchemaVersion
- Update csproj prop
    -  `<LatestSchemaVersion>` with latest version
    -  `EmbeddedResource` to generate the latest c# models 
- Full schema is auto generated from the Sql target build task
- Migration diff script is manually generated and should follow recommedations [here](https://github.com/microsoft/healthcare-shared-components/tree/main/src/Microsoft.Health.SqlServer/SqlSchemaScriptsGuidelines.md)

## Testing changes

- Ensure to run SqlServerSchemaUpgradeTests
- Make sure yours scripts are consistent with Full script SQL formatting. Test - GivenTwoSchemaInitializationMethods_WhenCreatingTwoDatabases_BothSchemasShouldBeEquivalent, covers the validation for any differences between snapshot and diff databases.

## Performance Checklist

  - Beware of computed columns - even persisted ones tend to have weird issues. They're fine for simple storage/retrieval.
  - Don't use CURSORS - if you need one, you're doing it wrong.
  - Avoid Triggers - they're really hard to get right.
  - Be careful with @tables - they work fine in Table Valued Parameters (TVPs) but otherwise the cardinality defaults to 1 which may be very bad.
  - Minimize usage of temporary tables - they're expensive to create/destroy
  - When using \#tables - make sure to create all indexes in place (i.e. do not change the table once created), otherwise it won't be cached.
  - No dynamic SQL - it causes all kinds of issues (security, compilations, temp tables...) - servicing scripts and WIT are the only place you should find it.
  - If there's only one good way to execute the query, hint it (see below for an example) so it cannot deviate. If there's a bad plan, the optimizer will find it, and you will get called.
  - Ensure secondary indexes are actually used - SQL has a DMV for that: sys.dm\_db\_index\_usage\_stats
  - Avoid OR's in WHERE or JOIN clauses if there's an index - you can achieve the same effect with a UNION

### Online Index Creation and page compression 

When we create new indexes or rebuild existing ones during database upgrade, it is very important that we pass **ONLINE=ON** option. Otherwise, SQL Azure will be holding an exclusive lock on the underlying table, which will block access to this table and may cause an LSI.
Here is an example:

``` sql
IF EXISTS (
    SELECT  *
    FROM    sys.indexes
    WHERE   name = 'IXC_PropertyDefinition_PropertyId'
            AND object_id = OBJECT_ID('PropertyDefinition')
            AND ignore_dup_key = 1
)
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX IXCl_PropertyDefinition_PropertyId ON PropertyDefinition (PartitionId, PropertyId ASC) INCLUDE (TypeId)
    WITH (DROP_EXISTING=ON, ONLINE=ON)
END
```
## Sql Migration 

https://github.com/microsoft/healthcare-shared-components/tree/main/src/Microsoft.Health.SqlServer/SqlSchemaScriptsGuidelines.md

## Sql Compatability

Binaries should be backcompatible with Latest and Latest-1 schema version atleast. 
