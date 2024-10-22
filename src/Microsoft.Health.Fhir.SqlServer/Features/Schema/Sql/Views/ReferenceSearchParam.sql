CREATE VIEW dbo.ReferenceSearchParam
AS
SELECT A.ResourceTypeId
      ,ResourceSurrogateId
      ,SearchParamId
      ,BaseUri
      ,ReferenceResourceTypeId
      ,ReferenceResourceId = B.ResourceId
      ,ReferenceResourceVersion
  FROM dbo.ReferenceSearchParams A
       LEFT OUTER JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ReferenceResourceTypeId AND B.ResourceIdInt = A.ReferenceResourceIdInt
GO
CREATE TRIGGER dbo.ReferenceSearchParamDel ON dbo.ReferenceSearchParam INSTEAD OF DELETE
AS
BEGIN
  DELETE FROM A
    FROM dbo.ReferenceSearchParams A
    WHERE EXISTS (SELECT * FROM Deleted B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)
END
GO
