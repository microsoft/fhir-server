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
