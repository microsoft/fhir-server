IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('StringSearchParam') AND name = 'IX_StringSearchParam_SearchParamId_Text_INCLUDE_TextOverflow_IsMin_IsMax')
  EXECUTE sp_rename 'StringSearchParam.IX_StringSearchParam_SearchParamId_Text_INCLUDE_TextOverflow_IsMin_IsMax', 'IX_SearchParamId_Text_INCLUDE_TextOverflow_IsMin_IsMax'
GO
IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenDateTimeCompositeSearchParam') AND name = 'IX_Code1_EndDateTime2_StartDateTime2_INCLUDE_SystemId1_WHERE_IsLongerThanADay2_1')
  EXECUTE sp_rename 'TokenDateTimeCompositeSearchParam.IX_Code1_EndDateTime2_StartDateTime2_INCLUDE_SystemId1_WHERE_IsLongerThanADay2_1', 'IX_SearchParamId_Code1_EndDateTime2_StartDateTime2_INCLUDE_SystemId1_WHERE_IsLongerThanADay2_1'
GO
DECLARE @Objects TABLE (Name varchar(100) PRIMARY KEY)
INSERT INTO @Objects SELECT name FROM sys.objects WHERE type = 'u' AND name LIKE '%SearchParam'

DECLARE @Tbl varchar(100)

WHILE EXISTS (SELECT * FROM @Objects)
BEGIN
  SET @Tbl = (SELECT TOP 1 Name FROM @Objects)

  IF EXISTS (SELECT * FROM sys.columns WHERE object_id = object_id(@Tbl) AND name = 'IsHistory')
  BEGIN
    IF EXISTS (SELECT * FROM sys.default_constraints WHERE name = 'DF_'+@Tbl+'_IsHistory')
      EXECUTE('ALTER TABLE dbo.'+@Tbl+' DROP CONSTRAINT DF_'+@Tbl+'_IsHistory')

    EXECUTE('ALTER TABLE dbo.'+@Tbl+' DROP COLUMN IsHistory')
  END

  DELETE FROM @Objects WHERE Name = @Tbl
END
GO
--IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenText') AND name = 'IX_SearchParamId_Text_ResourceSurrogateId')
--  CREATE INDEX IX_SearchParamId_Text_ResourceSurrogateId 
--    ON dbo.TokenText (SearchParamId, Text, ResourceSurrogateId)
--    WITH (DATA_COMPRESSION = PAGE)
--    ON PartitionScheme_ResourceTypeId (ResourceTypeId)
--GO
--IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenText') AND name = 'IX_TokenText_SearchParamId_Text')
--  DROP INDEX IX_TokenText_SearchParamId_Text ON dbo.TokenText
--GO
--IF EXISTS (SELECT * FROM sys.default_constraints WHERE name = 'DF_TokenText_IsHistory')
--  ALTER TABLE dbo.TokenText DROP CONSTRAINT DF_TokenText_IsHistory
--GO
--IF EXISTS (SELECT * FROM sys.columns WHERE object_id = object_id('TokenText') AND name = 'IsHistory')
--  ALTER TABLE dbo.TokenText DROP COLUMN IsHistory
--GO
