IF EXISTS (SELECT * FROM sys.objects WHERE type = 'u' AND name = 'TokenSearchParam')
  EXECUTE sp_rename 'TokenSearchParam', 'TokenSearchParam_Partitioned'
GO
IF object_id('CHK_TokenSearchParam_CodeOverflow') IS NOT NULL
  EXECUTE sp_rename 'CHK_TokenSearchParam_CodeOverflow', 'CHK_TokenSearchParam_Partitioned_CodeOverflow'
GO
IF EXISTS (SELECT * FROM sys.objects WHERE type = 'v' AND name = 'TokenSearchParam')
  DROP VIEW dbo.TokenSearchParam
GO
IF object_id('tempdb..#RTs') IS NOT NULL DROP TABLE #RTs
GO
DECLARE @CreateView varchar(max) = '
CREATE VIEW dbo.TokenSearchParam
AS
SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,SystemId,Code,CodeOverflow, IsHistory = convert(bit,0) FROM dbo.TokenSearchParam_Partitioned WHERE ResourceTypeId NOT IN (4,14,15,19,28,35,40,44,53,61,62,76,79,96,100,103,108,110,138)'
       ,@InsertTrigger varchar(max) = '
CREATE TRIGGER dbo.TokenSearchParamIns ON dbo.TokenSearchParam INSTEAD OF INSERT
AS
set nocount on
INSERT INTO dbo.TokenSearchParam_Partitioned 
        (ResourceTypeId,ResourceSurrogateId,SearchParamId,SystemId,Code,CodeOverflow) 
  SELECT ResourceTypeId,ResourceSurrogateId,SearchParamId,SystemId,Code,CodeOverflow
    FROM Inserted 
    WHERE ResourceTypeId NOT IN (4,14,15,19,28,35,40,44,53,61,62,76,79,96,100,103,108,110,138)'
       ,@DeleteTrigger varchar(max) = '
CREATE TRIGGER dbo.TokenSearchParamDel ON dbo.TokenSearchParam INSTEAD OF DELETE
AS
set nocount on
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

  IF object_id('dbo.TokenSearchParam_'+@RT) IS NULL
    EXECUTE dbo.SwitchPartitionsOut 'TokenSearchParam_Partitioned', 0, @RT, 'TokenSearchParam'
  
  IF NOT EXISTS (SELECT * FROM sys.default_constraints WHERE name = 'DF_TokenSearchParam_'+@RT+'_IsHistory')
    EXECUTE('ALTER TABLE dbo.TokenSearchParam_'+@RT+' ADD CONSTRAINT DF_TokenSearchParam_'+@RT+'_IsHistory DEFAULT 0 FOR IsHistory')
  
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
