EXECUTE sp_rename 'ReferenceSearchParam', 'ReferenceSearchParamTbl'
GO
CREATE OR ALTER VIEW dbo.ReferenceSearchParam
AS
SELECT A.ResourceTypeId
      ,ResourceSurrogateId
      ,SearchParamId
      ,BaseUri
      ,ReferenceResourceTypeId
      ,ReferenceResourceId = CASE WHEN A.ReferenceResourceId = '' THEN B.ResourceId ELSE A.ReferenceResourceId END
      ,ReferenceResourceVersion
  FROM dbo.ReferenceSearchParamTbl A
       LEFT OUTER JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ReferenceResourceTypeId AND B.ResourceIdInt = A.ReferenceResourceIdInt
GO
CREATE OR ALTER TRIGGER dbo.ReferenceSearchParamIns ON dbo.ReferenceSearchParam INSTEAD OF INSERT
AS
DECLARE @DummyTop bigint = 9223372036854775807
BEGIN
  INSERT INTO dbo.ReferenceSearchParamTbl
      (
           ResourceTypeId
          ,ResourceSurrogateId
          ,SearchParamId
          ,BaseUri
          ,ReferenceResourceTypeId
          ,ReferenceResourceIdInt
          ,ReferenceResourceVersion
      )
    SELECT A.ResourceTypeId
          ,ResourceSurrogateId
          ,SearchParamId
          ,BaseUri
          ,ReferenceResourceTypeId
          ,B.ResourceIdInt
          ,ReferenceResourceVersion
      FROM (SELECT TOP (@DummyTop) * FROM Inserted) A
           JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ReferenceResourceTypeId AND B.ResourceId = A.ReferenceResourceId
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
END
GO
CREATE OR ALTER TRIGGER dbo.ReferenceSearchParamUpd ON dbo.ReferenceSearchParam INSTEAD OF UPDATE
AS
BEGIN
  RAISERROR('Generic updates are not supported via ReferenceSearchParam view',18,127)
END
GO
CREATE OR ALTER TRIGGER dbo.ReferenceSearchParamDel ON dbo.ReferenceSearchParam INSTEAD OF DELETE
AS
BEGIN
  DELETE FROM A
    FROM dbo.ReferenceSearchParamTbl A
    WHERE EXISTS (SELECT * FROM Deleted B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId)
END
GO
