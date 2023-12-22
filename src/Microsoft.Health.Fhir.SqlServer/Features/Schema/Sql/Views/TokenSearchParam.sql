IF EXISTS (SELECT * FROM sys.objects WHERE type = 'u' AND name = 'TokenSearchParam')
  EXECUTE sp_rename 'TokenSearchParam', 'TokenSearchParam_Partitioned'
GO
IF EXISTS (SELECT * FROM sys.objects WHERE type = 'v' AND name = 'TokenSearchParam')
  DROP VIEW dbo.TokenSearchParam
GO
IF object_id('tempdb..#RTs') IS NOT NULL DROP TABLE #RTs
GO
DECLARE @Template varchar(max) = '
IF object_id(''TokenSearchParam_XXX'') IS NULL
  CREATE TABLE dbo.TokenSearchParam_XXX
  (
      ResourceTypeId       smallint     NOT NULL
     ,ResourceSurrogateId  bigint       NOT NULL
     ,SearchParamId        smallint     NOT NULL
     ,SystemId             int          NULL
     ,Code                 varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
     ,CodeOverflow         varchar(max) COLLATE Latin1_General_100_CS_AS NULL
   
     ,CONSTRAINT CHK_TokenSearchParam_XXX_CodeOverflow CHECK (len(Code) = 256 OR CodeOverflow IS NULL)
     ,CONSTRAINT CHK_TokenSearchParam_XXX_ResourceTypeId_XXX CHECK (ResourceTypeId = XXX)
  )

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id(''TokenSearchParam_XXX'') AND name = ''IXC_ResourceSurrogateId_SearchParamId'')
  CREATE CLUSTERED INDEX IXC_ResourceSurrogateId_SearchParamId ON dbo.TokenSearchParam_XXX (ResourceSurrogateId, SearchParamId) 
    WITH (DATA_COMPRESSION = PAGE)

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id(''TokenSearchParam_XXX'') AND name = ''IX_SearchParamId_Code_INCLUDE_SystemId'')
  CREATE INDEX IX_SearchParamId_Code_INCLUDE_SystemId ON dbo.TokenSearchParam_XXX (SearchParamId, Code) INCLUDE (SystemId) 
    WITH (DATA_COMPRESSION = PAGE)'
       ,@CreateTable varchar(max)
       ,@CreateView varchar(max) = '
CREATE VIEW dbo.TokenSearchParam
AS
SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,SystemId,Code,CodeOverflow, IsHistory = convert(bit,0) FROM dbo.TokenSearchParam_Partitioned'
       ,@InsertTrigger varchar(max) = '
CREATE TRIGGER dbo.TokenSearchParamIns ON dbo.TokenSearchParam INSTEAD OF INSERT
AS
INSERT INTO dbo.TokenSearchParam_Partitioned 
        (ResourceTypeId,ResourceSurrogateId,SearchParamId,SystemId,Code,CodeOverflow) 
  SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,SystemId,Code,CodeOverflow
    FROM Inserted 
    WHERE ResourceTypeId NOT IN (4,14,15,19,28,35,40,44,53,61,62,76,79,96,100,103,108,110,138)'
       ,@DeleteTrigger varchar(max) = '
CREATE TRIGGER dbo.TokenSearchParamDel ON dbo.TokenSearchParam INSTEAD OF DELETE
AS
DELETE FROM dbo.TokenSearchParam_Partitioned WHERE EXISTS (SELECT * FROM (SELECT T = ResourceTypeId, S = ResourceSurrogateId FROM Deleted) A WHERE T = ResourceTypeId AND S = ResourceSurrogateId)'

SELECT RT
  INTO #RTs
  FROM (
SELECT RT = 4
UNION SELECT 14
UNION SELECT 15
UNION SELECT 19
UNION SELECT 28
UNION SELECT 35
UNION SELECT 40
UNION SELECT 44
UNION SELECT 53
UNION SELECT 61
UNION SELECT 62
UNION SELECT 76
UNION SELECT 79
UNION SELECT 96
UNION SELECT 100
UNION SELECT 103
UNION SELECT 108
UNION SELECT 110
UNION SELECT 138
      ) A

DECLARE @RT varchar(100)
WHILE EXISTS (SELECT * FROM #RTs)
BEGIN
  SET @RT = (SELECT TOP 1 RT FROM #RTs)
  SET @CreateTable = @Template
  SET @CreateTable = replace(@CreateTable,'XXX',@RT)
  --PRINT @CreateTable
  EXECUTE(@CreateTable)
  
  SET @CreateView = @CreateView + '
UNION ALL SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,SystemId,Code,CodeOverflow, IsHistory = convert(bit,0) FROM dbo.TokenSearchParam_'+@RT

  SET @InsertTrigger = @InsertTrigger + '
INSERT INTO dbo.TokenSearchParam_'+@RT+' 
        (ResourceTypeId,ResourceSurrogateId,SearchParamId,SystemId,Code,CodeOverflow) 
  SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,SystemId,Code,CodeOverflow 
    FROM Inserted 
    WHERE ResourceTypeId = '+@RT

  SET @DeleteTrigger = @DeleteTrigger + '
DELETE FROM dbo.TokenSearchParam_'+@RT+' WHERE ResourceTypeId = '+@RT+' AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM Deleted WHERE ResourceTypeId = '+@RT+')'

  DELETE FROM #RTs WHERE RT = @RT
END

--PRINT @CreateView
EXECUTE(@CreateView)
EXECUTE(@InsertTrigger)
EXECUTE(@DeleteTrigger)
GO
