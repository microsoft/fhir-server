CREATE VIEW dbo.ReferenceSearchParam
AS
SELECT A.ResourceTypeId
      ,ResourceSurrogateId
      ,SearchParamId
      ,BaseUri
      ,ReferenceResourceTypeId
      ,ReferenceResourceId = B.ResourceId
      ,IsResourceRef
  FROM dbo.ResourceReferenceSearchParams A
       LEFT OUTER JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ReferenceResourceTypeId AND B.ResourceIdInt = A.ReferenceResourceIdInt
UNION ALL
SELECT ResourceTypeId
      ,ResourceSurrogateId
      ,SearchParamId
      ,BaseUri
      ,NULL
      ,ReferenceResourceId
      ,IsResourceRef
  FROM dbo.StringReferenceSearchParams
GO
CREATE TRIGGER dbo.ReferenceSearchParamDel ON dbo.ReferenceSearchParam INSTEAD OF DELETE
AS
BEGIN
  DELETE FROM A
    FROM dbo.ResourceReferenceSearchParams A
    WHERE EXISTS (SELECT * FROM Deleted B WHERE B.IsResourceRef = 1 AND B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)

  DELETE FROM A
    FROM dbo.StringReferenceSearchParams A
    WHERE EXISTS (SELECT * FROM Deleted B WHERE B.IsResourceRef = 0 AND B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)
END
GO
