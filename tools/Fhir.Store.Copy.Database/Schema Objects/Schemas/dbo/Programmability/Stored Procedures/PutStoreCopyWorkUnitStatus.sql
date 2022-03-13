--DROP PROCEDURE dbo.PutStoreCopyWorkUnitStatus
GO
CREATE PROCEDURE dbo.PutStoreCopyWorkUnitStatus @ResourceTypeId smallint, @UnitId int, @Failed bit
AS
set nocount on
DECLARE @SP varchar(100) = 'PutStoreCopyWorkUnitStatus'
       ,@Mode varchar(100) = 'RT='+convert(varchar,@ResourceTypeId)+' U='+convert(varchar,@UnitId)+' F='+convert(varchar,@Failed)
       ,@st datetime = getUTCdate()

BEGIN TRY
  UPDATE dbo.StoreCopyWorkQueue
    SET EndDate = getUTCdate()
       ,Status = CASE WHEN @Failed = 1 THEN 3 ELSE 2 END -- 2:completed with success  3:completed with failure  
    WHERE ResourceTypeId = @ResourceTypeId
      AND UnitId = @UnitId
      AND Status = 1
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error'
END CATCH
GO
