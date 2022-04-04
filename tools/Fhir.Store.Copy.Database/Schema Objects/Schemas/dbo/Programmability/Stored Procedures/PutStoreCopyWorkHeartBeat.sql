--DROP PROCEDURE dbo.PutStoreCopyWorkHeartBeat
GO
CREATE PROCEDURE dbo.PutStoreCopyWorkHeartBeat @PartitionId tinyint, @UnitId int, @ResourceCount int = NULL
AS
set nocount on
DECLARE @SP varchar(100) = 'PutStoreCopyWorkHeartBeat'
       ,@Mode varchar(100)
       ,@st datetime = getUTCdate()

SET @Mode = 'P='+convert(varchar,@PartitionId)+' U='+convert(varchar,@UnitId)+' R='+isnull(convert(varchar,@ResourceCount),'NULL')

BEGIN TRY
  UPDATE dbo.StoreCopyWorkQueue
    SET HeartBeatDate = getUTCdate()
       ,ResourceCount = isnull(@ResourceCount,ResourceCount)
    WHERE PartitionId = @PartitionId
      AND UnitId = @UnitId
      AND Status = 1
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error'
END CATCH
GO
