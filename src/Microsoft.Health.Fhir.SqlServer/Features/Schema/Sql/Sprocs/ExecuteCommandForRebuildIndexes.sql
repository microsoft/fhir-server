--IF object_id('ExecuteCommandForRebuildIndexes') IS NOT NULL DROP PROCEDURE dbo.ExecuteCommandForRebuildIndexes
GO
CREATE PROCEDURE dbo.ExecuteCommandForRebuildIndexes @Tbl varchar(100), @Ind varchar(1000), @Cmd varchar(max)
WITH EXECUTE AS 'dbo'
AS
set nocount on
DECLARE @SP varchar(100) = 'ExecuteCommandForRebuildIndexes' 
       ,@Mode varchar(200) = 'Tbl='+isnull(@Tbl,'NULL')
       ,@st datetime
       ,@Retries int = 0
       ,@Action varchar(100)
       ,@msg varchar(1000)

RetryOnTempdbError:

BEGIN TRY
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start',@Text=@Cmd

  SET @st = getUTCdate()

  IF @Tbl IS NULL RAISERROR('@Tbl IS NULL',18,127)
  IF @Cmd IS NULL RAISERROR('@Cmd IS NULL',18,127)

  SET @Action = CASE 
                  WHEN @Cmd LIKE 'UPDATE STAT%' THEN 'Update statistics'
                  WHEN @Cmd LIKE 'CREATE%INDEX%' THEN 'Create Index'
                  WHEN @Cmd LIKE 'ALTER%INDEX%REBUILD%' THEN 'Rebuild Index'
                  WHEN @Cmd LIKE 'ALTER%TABLE%ADD%' THEN 'Add Constraint'
                END
  IF @Action IS NULL 
  BEGIN
    SET @msg = 'Not supported command = '+convert(varchar(900),@Cmd)
    RAISERROR(@msg,18,127)
  END

  IF @Action = 'Create Index' WAITFOR DELAY '00:00:05'

  EXECUTE(@Cmd)
  SELECT @Ind;

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Action=@Action,@Status='End',@Start=@st,@Text=@Cmd
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  IF error_number() = 40544 -- '%database ''tempdb'' has reached its size quota%'
  BEGIN
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st,@ReRaisError=0,@Retry=@Retries
    SET @Retries = @Retries + 1
    IF @Tbl = 'TokenText_96' 
      WAITFOR DELAY '01:00:00' -- 1 hour
    ELSE 
      WAITFOR DELAY '00:10:00' -- 10 minutes
    GOTO RetryOnTempdbError
  END
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
