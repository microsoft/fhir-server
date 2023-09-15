CREATE VIEW dbo.Resource
AS
SELECT ResourceTypeId
      ,ResourceSurrogateId
      ,ResourceId
      ,Version
      ,IsHistory
      ,IsDeleted
      ,RequestMethod
      ,RawResource
      ,IsRawResourceMetaSet
      ,SearchParamHash
      ,TransactionId
      ,HistoryTransactionId
  FROM dbo.ResourceHistory
UNION ALL
SELECT ResourceTypeId
      ,ResourceSurrogateId
      ,ResourceId
      ,Version
      ,IsHistory
      ,IsDeleted
      ,RequestMethod
      ,RawResource
      ,IsRawResourceMetaSet
      ,SearchParamHash
      ,TransactionId
      ,NULL
  FROM dbo.ResourceCurrent
GO
CREATE TRIGGER dbo.ResourceIns ON dbo.Resource INSTEAD OF INSERT
AS
BEGIN
  INSERT INTO dbo.ResourceCurrent
      (
           ResourceTypeId
          ,ResourceSurrogateId
          ,ResourceId
          ,Version
          ,IsDeleted
          ,RequestMethod
          ,RawResource
          ,IsRawResourceMetaSet
          ,SearchParamHash
          ,TransactionId
      )
    SELECT ResourceTypeId
          ,ResourceSurrogateId
          ,ResourceId
          ,Version
          ,IsDeleted
          ,RequestMethod
          ,RawResource
          ,IsRawResourceMetaSet
          ,SearchParamHash
          ,TransactionId
      FROM Inserted
      WHERE IsHistory = 0

  INSERT INTO dbo.ResourceHistory
      (
           ResourceTypeId
          ,ResourceSurrogateId
          ,ResourceId
          ,Version
          ,IsDeleted
          ,RequestMethod
          ,RawResource
          ,IsRawResourceMetaSet
          ,SearchParamHash
          ,TransactionId
          ,HistoryTransactionId
      )
    SELECT ResourceTypeId
          ,ResourceSurrogateId
          ,ResourceId
          ,Version
          ,IsDeleted
          ,RequestMethod
          ,RawResource
          ,IsRawResourceMetaSet
          ,SearchParamHash
          ,TransactionId
          ,HistoryTransactionId
      FROM Inserted
      WHERE IsHistory = 1
END
GO
CREATE TRIGGER dbo.ResourceUpd ON dbo.Resource INSTEAD OF UPDATE
AS
BEGIN
  IF UPDATE(SearchParamHash) AND NOT UPDATE(IsHistory)
  BEGIN
    UPDATE B
      SET SearchParamHash = A.SearchParamHash -- this is the only update we support
      FROM Inserted A
           JOIN dbo.ResourceCurrent B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
      WHERE A.IsHistory = 0
    
    RETURN
  END

  IF NOT UPDATE(IsHistory)
    RAISERROR('Generic updates are not supported via Resource view',18,127)

  DELETE FROM A
    FROM dbo.ResourceCurrent A
    WHERE EXISTS (SELECT * FROM Inserted B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId AND B.IsHistory = 1)

  INSERT INTO dbo.ResourceHistory
      (
           ResourceTypeId
          ,ResourceSurrogateId
          ,ResourceId
          ,Version
          ,IsDeleted
          ,RequestMethod
          ,RawResource
          ,IsRawResourceMetaSet
          ,SearchParamHash
          ,TransactionId
          ,HistoryTransactionId
      )
    SELECT ResourceTypeId
          ,ResourceSurrogateId
          ,ResourceId
          ,Version
          ,IsDeleted
          ,RequestMethod
          ,RawResource
          ,IsRawResourceMetaSet
          ,SearchParamHash
          ,TransactionId
          ,HistoryTransactionId
      FROM Inserted
      WHERE IsHistory = 1
END
GO
CREATE TRIGGER dbo.ResourceDel ON dbo.Resource INSTEAD OF DELETE
AS
BEGIN
  DELETE FROM A
    FROM dbo.ResourceCurrent A
    WHERE EXISTS (SELECT * FROM Deleted B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId AND B.IsHistory = 0)

  DELETE FROM A
    FROM dbo.ResourceHistory A
    WHERE EXISTS (SELECT * FROM Deleted B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId AND B.IsHistory = 1)
END
GO
-- This should gurantee SQL access stability when there are no resources. It should work for both view and table.
-- Resources are filtered out on 2 levels: by ResoureTypeId set to not existing value 0 and by RawResource set to invisible value 0x01
INSERT INTO dbo.Resource
    (
         ResourceTypeId
        ,ResourceSurrogateId
        ,ResourceId
        ,Version
        ,IsDeleted
        ,RequestMethod
        ,RawResource
        ,IsRawResourceMetaSet
        ,SearchParamHash
        ,TransactionId
        ,IsHistory
    )
  SELECT ResourceTypeId = 0
        ,ResourceSurrogateId = SurrId
        ,ResourceId = newid()
        ,Version = 0
        ,IsDeleted = 0
        ,RequestMethod = NULL
        ,RawResource = 0x01
        ,IsRawResourceMetaSet = 0
        ,SearchParamHash = NULL
        ,TransactionId = NULL
        ,IsHistory = 0
    FROM (SELECT TOP 100 SurrId = (row_number() OVER (ORDER BY colid)) + datediff_big(millisecond,'0001-01-01',sysUTCdatetime()) * 80000 FROM syscolumns) A
    WHERE NOT EXISTS (SELECT * FROM dbo.Resource WHERE IsHistory = 0 AND ResourceTypeId = 0 AND ResourceSurrogateId = SurrId)
GO
