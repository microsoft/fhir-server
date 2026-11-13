CREATE PROCEDURE dbo.CreateGranularSecondaryIndexes @TableName varchar(128), @ResourceTypeId smallint, @SearchParamId smallint
WITH EXECUTE AS 'dbo'
AS
set nocount on

DECLARE @SP varchar(100) = object_name(@@procid)
       ,@st datetime = getUTCdate()
       ,@ObjectId int = object_id(@TableName)
       ,@ResourceTypeIdChar varchar(6) = convert(varchar,@ResourceTypeId)
       ,@SearchParamIdChar varchar(6) = convert(varchar,@SearchParamId)
       ,@IndexName sysname
       ,@IndexId int
       ,@Sql nvarchar(max)

DECLARE @Mode varchar(200) = 'T='+@TableName+' RT='+@ResourceTypeIdChar+' SP='+@SearchParamIdChar

BEGIN TRY
  IF @@trancount <> 0
    RAISERROR('Granular secondary indexes must be created outside transaction.', 18, 128)
  
  IF @ObjectId IS NULL OR @ResourceTypeId IS NULL OR @SearchParamId IS NULL OR NOT (@TableName LIKE '%SearchParam' OR @TableName = 'TokenText')
    RAISERROR('A search parameter table, resource type ID, and search parameter ID are required.', 18, 128)

  DECLARE @Indexes TABLE (IndexId int PRIMARY KEY, TemplateName sysname NOT NULL, IndexName sysname NOT NULL UNIQUE, Compression varchar(100), Sql nvarchar(max))
  INSERT INTO @Indexes
    SELECT I.index_id, I.name, N.IndexName, P.PropertyValue
          ,'CREATE'+CASE WHEN I.is_unique = 1 THEN ' UNIQUE' ELSE '' END +' INDEX '+N.IndexName+' ON dbo.'+@TableName+' ('+C.KeyColumns+')'
           +CASE WHEN C.IncludeColumns IS NOT NULL THEN ' INCLUDE ('+C.IncludeColumns+')' ELSE '' END
           +' WHERE ResourceTypeId = '+@ResourceTypeIdChar+' AND SearchParamId = '+@SearchParamIdChar
           +CASE WHEN I.filter_definition IS NOT NULL THEN ' AND ('+I.filter_definition+')' ELSE '' END
           +' WITH (DATA_COMPRESSION = '+IP.PropertyValue+')'
      FROM sys.indexes I
           CROSS APPLY (SELECT IndexName = replace(I.name,'_SearchParamId','')+'_WHERE_RT_'+@ResourceTypeIdChar+'_SP_'+@SearchParamIdChar) N
           LEFT JOIN dbo.IndexProperties P ON P.TableName = @TableName AND P.IndexName = I.name AND P.PropertyName = 'DATA_COMPRESSION'
           CROSS APPLY
           (SELECT KeyColumns = string_agg(convert(nvarchar(max),CASE WHEN IC.key_ordinal > 0 AND C.name <> 'SearchParamId' THEN C.name+CASE WHEN IC.is_descending_key = 1 THEN ' DESC' ELSE '' END END), ',') WITHIN GROUP (ORDER BY IC.key_ordinal, IC.index_column_id)
                  ,IncludeColumns = string_agg(convert(nvarchar(max),CASE WHEN IC.is_included_column = 1 THEN C.name END), ',') WITHIN GROUP (ORDER BY IC.key_ordinal, IC.index_column_id)
              FROM sys.index_columns IC JOIN sys.columns C ON C.object_id = IC.object_id AND C.column_id = IC.column_id
              WHERE IC.object_id = I.object_id AND IC.index_id = I.index_id
           ) C
      WHERE I.object_id = @ObjectId
        AND I.type = 2
        AND I.is_disabled = 1
        AND I.is_primary_key = 0
        AND I.is_unique_constraint = 0

  IF EXISTS (SELECT * FROM @Indexes WHERE Compression IS NULL)
  BEGIN
    SELECT TOP 1 @IndexName = TemplateName FROM @Indexes WHERE Compression IS NULL ORDER BY IndexId
    DECLARE @ErrorMessage varchar(2048) = 'Missing DATA_COMPRESSION in IndexProperties for '+@TableName+'.'+@IndexName;
    RAISERROR(@ErrorMessage, 18, 128)
  END

  IF EXISTS (SELECT * FROM @Indexes WHERE Sql IS NULL)
    RAISERROR('Could not generate SQL for granular index.', 18, 128)

  WHILE EXISTS (SELECT * FROM @Indexes)
  BEGIN
    SELECT TOP 1 @IndexId = IndexId, @IndexName = IndexName, @Sql = Sql FROM @Indexes ORDER BY IndexId
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = @ObjectId AND name = @IndexName)
    BEGIN TRY
      EXECUTE sys.sp_executesql @Sql
      EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Action='Create Index',@Target=@TableName,@Start=@st,@Text=@Sql
    END TRY
    BEGIN CATCH
      IF error_number() <> 1913
        THROW;
      ELSE 
        EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Action='Index Exists',@Target=@TableName,@Start=@st,@Text=@Sql
    END CATCH
    DELETE FROM @Indexes WHERE IndexId = @IndexId
  END

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st
END TRY
BEGIN CATCH
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
INSERT INTO dbo.Parameters (Id, Char) SELECT 'CreateGranularSecondaryIndexes','LogEvent'
GO
