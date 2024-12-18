CREATE VIEW dbo.CurrentResource
AS 
SELECT A.ResourceTypeId
      ,A.ResourceSurrogateId
      ,ResourceId
      ,A.ResourceIdInt
      ,Version
      ,IsHistory
      ,IsDeleted
      ,RequestMethod
      ,RawResource
      ,IsRawResourceMetaSet
      ,SearchParamHash
      ,TransactionId 
      ,HistoryTransactionId
      ,FileId
      ,OffsetInFile
  FROM dbo.CurrentResources A
       LEFT OUTER JOIN dbo.RawResources B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
       LEFT OUTER JOIN dbo.ResourceIdIntMap C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceIdInt = A.ResourceIdInt
GO
