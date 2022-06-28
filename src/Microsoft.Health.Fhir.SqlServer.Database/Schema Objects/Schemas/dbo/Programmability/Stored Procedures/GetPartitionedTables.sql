--IF object_id('GetPartitionedTables') IS NOT NULL DROP PROCEDURE dbo.GetPartitionedTables
GO
CREATE PROCEDURE dbo.GetPartitionedTables @IncludeNotDisabled bit, @IncludeNotSupported bit
WITH EXECUTE AS 'dbo'
AS
set nocount on
DECLARE @SP varchar(100) = 'GetPartitionedTables'
       ,@Mode varchar(100) = 'PS=PartitionScheme_ResourceTypeId D='+isnull(convert(varchar,@IncludeNotDisabled),'NULL')+' S='+isnull(convert(varchar,@IncludeNotSupported),'NULL')
       ,@st datetime = getUTCdate()

DECLARE @NotSupportedTables TABLE (id int PRIMARY KEY)

BEGIN TRY
  INSERT INTO @NotSupportedTables
    SELECT DISTINCT O.object_id
      FROM sys.indexes I
           JOIN sys.objects O ON O.object_id = I.object_id
      WHERE O.type = 'u'
        AND EXISTS (SELECT * FROM sys.partition_schemes PS WHERE PS.data_space_id = I.data_space_id AND name = 'PartitionScheme_ResourceTypeId')
        -- table is supported if all indexes contain ResourceTypeId as key column and all indexes are partitioned on the same scheme
        AND (NOT EXISTS 
               (SELECT * 
                  FROM sys.index_columns IC JOIN sys.columns C ON C.object_id = IC.object_id AND C.column_id = IC.column_id 
                  WHERE IC.object_id = I.object_id
                    AND IC.index_id = I.index_id
                    AND IC.key_ordinal > 0
                    AND IC.is_included_column = 0 
                    AND C.name = 'ResourceTypeId'
               )
             OR 
             EXISTS 
               (SELECT * 
                  FROM sys.indexes NSI 
                  WHERE NSI.object_id = O.object_id 
                    AND NOT EXISTS (SELECT * FROM sys.partition_schemes PS WHERE PS.data_space_id = NSI.data_space_id AND name = 'PartitionScheme_ResourceTypeId')
               )
            )

  SELECT convert(varchar(100),O.name), convert(bit,CASE WHEN EXISTS (SELECT * FROM @NotSupportedTables NSI WHERE NSI.id = O.object_id) THEN 0 ELSE 1 END)
    FROM sys.indexes I
         JOIN sys.objects O ON O.object_id = I.object_id
    WHERE O.type = 'u'
      AND I.index_id IN (0,1)
      AND EXISTS (SELECT * FROM sys.partition_schemes PS WHERE PS.data_space_id = I.data_space_id AND name = 'PartitionScheme_ResourceTypeId')
      AND EXISTS (SELECT * FROM sys.index_columns IC JOIN sys.columns C ON C.object_id = I.object_id AND C.column_id = IC.column_id AND IC.is_included_column = 0 AND C.name = 'ResourceTypeId')
      AND (@IncludeNotSupported = 1 
           OR NOT EXISTS (SELECT * FROM @NotSupportedTables NSI WHERE NSI.id = O.object_id)
          )
      AND (@IncludeNotDisabled = 1 OR EXISTS (SELECT * FROM sys.indexes D WHERE D.object_id = O.object_id AND D.is_disabled = 1))
    ORDER BY 1

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
