--IF object_id('SwitchPartitionsOutAllTables') IS NOT NULL DROP PROCEDURE dbo.SwitchPartitionsOutAllTables
GO
CREATE PROCEDURE dbo.SwitchPartitionsOutAllTables @RebuildClustered bit
WITH EXECUTE AS SELF
AS
set nocount on
DECLARE @SP varchar(100) = 'SwitchPartitionsOutAllTables'
       ,@Mode varchar(200) = 'PS=PartitionScheme_ResourceTypeId ND='+isnull(convert(varchar,@RebuildClustered),'NULL')
       ,@st datetime = getUTCdate()
       ,@Tbl varchar(100)

BEGIN TRY
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start'

  DECLARE @Tables TABLE (name varchar(100) PRIMARY KEY, supported bit)
  INSERT INTO @Tables EXECUTE dbo.GetPartitionedTables @IncludeNotDisabled = @RebuildClustered, @IncludeNotSupported = 0
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='@Tables',@Action='Insert',@Rows=@@rowcount

  WHILE EXISTS (SELECT * FROM @Tables)
  BEGIN
    SET @Tbl = (SELECT TOP 1 name FROM @Tables ORDER BY name)

    EXECUTE dbo.SwitchPartitionsOut @Tbl = @Tbl, @RebuildClustered = @RebuildClustered
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='SwitchPartitionsOut',@Action='Execute',@Text=@Tbl

    DELETE FROM @Tables WHERE name = @Tbl
  END

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
