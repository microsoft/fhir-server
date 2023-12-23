IF EXISTS (SELECT * FROM sys.objects WHERE type = 'u' AND name = 'StringSearchParam')
  EXECUTE sp_rename 'StringSearchParam', 'StringSearchParam_Partitioned'
GO
IF EXISTS (SELECT * FROM sys.objects WHERE type = 'v' AND name = 'StringSearchParam')
  DROP VIEW dbo.StringSearchParam
GO
IF object_id('tempdb..#RTs') IS NOT NULL DROP TABLE #RTs
GO
DECLARE @CreateView varchar(max) = '
CREATE VIEW dbo.StringSearchParam
AS
SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,Text,TextOverflow,IsMin,IsMax,IsHistory = convert(bit,0) FROM dbo.StringSearchParam_Partitioned WHERE ResourceTypeId NOT IN (4,14,15,19,28,35,40,44,53,61,62,76,79,96,100,103,108,110,138)'
       ,@InsertTrigger varchar(max) = '
CREATE TRIGGER dbo.StringSearchParamIns ON dbo.StringSearchParam INSTEAD OF INSERT
AS
set nocount on
INSERT INTO dbo.StringSearchParam_Partitioned 
        (ResourceTypeId,ResourceSurrogateId,SearchParamId,Text,TextOverflow,IsMin,IsMax) 
  SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,Text,TextOverflow,IsMin,IsMax 
    FROM Inserted 
    WHERE ResourceTypeId NOT IN (4,14,15,19,28,35,40,44,53,61,62,76,79,96,100,103,108,110,138)'
       ,@DeleteTrigger varchar(max) = '
CREATE TRIGGER dbo.StringSearchParamDel ON dbo.StringSearchParam INSTEAD OF DELETE
AS
set nocount on
DELETE FROM dbo.StringSearchParam_Partitioned WHERE EXISTS (SELECT * FROM (SELECT T = ResourceTypeId, S = ResourceSurrogateId FROM Deleted) A WHERE T = ResourceTypeId AND S = ResourceSurrogateId)'

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

  IF object_id('dbo.StringSearchParam_'+@RT) IS NULL
    EXECUTE dbo.SwitchPartitionsOut 'StringSearchParam_Partitioned', 0, @RT, 'StringSearchParam'
  
  IF NOT EXISTS (SELECT * FROM sys.default_constraints WHERE name = 'DF_StringSearchParam_'+@RT+'_IsHistory')
    EXECUTE('ALTER TABLE dbo.StringSearchParam_'+@RT+' ADD CONSTRAINT DF_StringSearchParam_'+@RT+'_IsHistory DEFAULT 0 FOR IsHistory')

  SET @CreateView = @CreateView + '
UNION ALL SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,Text,TextOverflow,IsMin,IsMax,IsHistory = convert(bit,0) FROM dbo.StringSearchParam_'+@RT

  SET @InsertTrigger = @InsertTrigger + '
INSERT INTO dbo.StringSearchParam_'+@RT+' 
        (ResourceTypeId,ResourceSurrogateId,SearchParamId,Text,TextOverflow,IsMin,IsMax) 
  SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,Text,TextOverflow,IsMin,IsMax 
    FROM Inserted 
    WHERE ResourceTypeId = '+@RT

  SET @DeleteTrigger = @DeleteTrigger + '
DELETE FROM dbo.StringSearchParam_'+@RT+' WHERE ResourceTypeId = '+@RT+' AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM Deleted WHERE ResourceTypeId = '+@RT+')'

  DELETE FROM #RTs WHERE RT = @RT
END

--PRINT @CreateView
EXECUTE(@CreateView)
EXECUTE(@InsertTrigger)
EXECUTE(@DeleteTrigger)
GO
