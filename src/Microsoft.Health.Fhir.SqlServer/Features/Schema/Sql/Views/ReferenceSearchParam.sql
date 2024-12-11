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
