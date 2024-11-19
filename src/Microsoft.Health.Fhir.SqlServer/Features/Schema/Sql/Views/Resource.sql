CREATE VIEW dbo.Resource
AS 
SELECT A.ResourceTypeId
      ,A.ResourceSurrogateId
      ,ResourceId
      ,A.ResourceIdInt
      ,Version
      ,IsHistory
      ,IsDeleted
      ,RequestMethod
      ,B.RawResource
      ,IsRawResourceMetaSet
      ,SearchParamHash
      ,TransactionId 
      ,HistoryTransactionId
      ,FileId
      ,OffsetInFile
  FROM dbo.CurrentResources A
       LEFT OUTER JOIN dbo.RawResources B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
       LEFT OUTER JOIN dbo.ResourceIdIntMap C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceIdInt = A.ResourceIdInt
UNION ALL
SELECT A.ResourceTypeId
      ,A.ResourceSurrogateId
      ,ResourceId
      ,A.ResourceIdInt
      ,Version
      ,IsHistory
      ,IsDeleted
      ,RequestMethod
      ,B.RawResource
      ,IsRawResourceMetaSet
      ,SearchParamHash
      ,TransactionId 
      ,HistoryTransactionId
      ,FileId
      ,OffsetInFile
  FROM dbo.HistoryResources A
       LEFT OUTER JOIN dbo.RawResources B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
       LEFT OUTER JOIN dbo.ResourceIdIntMap C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceIdInt = A.ResourceIdInt
GO
CREATE TRIGGER dbo.ResourceIns ON dbo.Resource INSTEAD OF INSERT
AS
BEGIN
  INSERT INTO dbo.RawResources
         ( ResourceTypeId, ResourceSurrogateId, RawResource )
    SELECT ResourceTypeId, ResourceSurrogateId, RawResource
      FROM Inserted
      WHERE RawResource IS NOT NULL

  INSERT INTO dbo.CurrentResources
         ( ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile )
    SELECT ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile
      FROM Inserted
      WHERE IsHistory = 0

  INSERT INTO dbo.HistoryResources
         ( ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile )
    SELECT ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile
      FROM Inserted
      WHERE IsHistory = 1
END
GO
CREATE TRIGGER dbo.ResourceUpd ON dbo.Resource INSTEAD OF UPDATE
AS
BEGIN
  IF UPDATE(IsDeleted) AND UPDATE(RawResource) AND UPDATE(SearchParamHash) AND UPDATE(HistoryTransactionId) AND NOT UPDATE(IsHistory) -- hard delete resource
  BEGIN
    UPDATE B
      SET RawResource = A.RawResource
      FROM Inserted A
           JOIN dbo.RawResources B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
    
    IF @@rowcount = 0
      INSERT INTO dbo.RawResources
             ( ResourceTypeId, ResourceSurrogateId, RawResource )
        SELECT ResourceTypeId, ResourceSurrogateId, RawResource
          FROM Inserted
          WHERE RawResource IS NOT NULL

    UPDATE B
      SET IsDeleted = A.IsDeleted
         ,SearchParamHash = A.SearchParamHash
         ,HistoryTransactionId = A.HistoryTransactionId
      FROM Inserted A
           JOIN dbo.CurrentResources B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
    RETURN
  END

  IF UPDATE(SearchParamHash) AND NOT UPDATE(IsHistory) -- reindex
  BEGIN
    UPDATE B
      SET SearchParamHash = A.SearchParamHash
      FROM Inserted A
           JOIN dbo.CurrentResources B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
      WHERE A.IsHistory = 0
    
    RETURN
  END

  IF UPDATE(TransactionId) AND NOT UPDATE(IsHistory) -- cleanup trans
  BEGIN
    UPDATE B
      SET TransactionId = A.TransactionId
      FROM Inserted A
           JOIN dbo.CurrentResources B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId AND B.IsHistory = 0

    UPDATE B
      SET TransactionId = A.TransactionId
      FROM Inserted A
           JOIN dbo.HistoryResources B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId AND B.IsHistory = 1
    
    RETURN
  END

  IF UPDATE(RawResource) -- invisible records
  BEGIN
    UPDATE B
      SET RawResource = A.RawResource
      FROM Inserted A
           JOIN dbo.RawResources B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId

    IF @@rowcount = 0
      INSERT INTO dbo.RawResources
             ( ResourceTypeId, ResourceSurrogateId, RawResource )
        SELECT ResourceTypeId, ResourceSurrogateId, RawResource
          FROM Inserted
          WHERE RawResource IS NOT NULL
  END

  IF NOT UPDATE(IsHistory)
    RAISERROR('Generic updates are not supported via Resource view',18,127)

  DELETE FROM A
    FROM dbo.CurrentResources A
    WHERE EXISTS (SELECT * FROM Inserted B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId AND B.IsHistory = 1)

  INSERT INTO dbo.HistoryResources
         ( ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile )
    SELECT ResourceTypeId, ResourceSurrogateId, ResourceIdInt, Version, IsDeleted, RequestMethod, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId, FileId, OffsetInFile
      FROM Inserted
      WHERE IsHistory = 1
END
GO
CREATE TRIGGER dbo.ResourceDel ON dbo.Resource INSTEAD OF DELETE
AS
BEGIN
  DELETE FROM A
    FROM dbo.CurrentResources A
    WHERE EXISTS (SELECT * FROM Deleted B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId AND B.IsHistory = 0)

  DELETE FROM A
    FROM dbo.HistoryResources A
    WHERE EXISTS (SELECT * FROM Deleted B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId AND B.IsHistory = 1)

  DELETE FROM A
    FROM dbo.RawResources A
    WHERE EXISTS (SELECT * FROM Deleted B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)
END
GO
