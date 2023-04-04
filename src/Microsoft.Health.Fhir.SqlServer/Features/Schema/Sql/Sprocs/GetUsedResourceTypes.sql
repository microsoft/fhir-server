--DROP PROCEDURE dbo.GetUsedResourceTypes
GO
CREATE PROCEDURE dbo.GetUsedResourceTypes
AS
set nocount on
DECLARE @SP varchar(100) = 'GetUsedResourceTypes'
       ,@Mode varchar(100) = ''
       ,@st datetime = getUTCdate()

BEGIN TRY
  SELECT ResourceTypeId, Name
    FROM dbo.ResourceType A
    WHERE EXISTS (SELECT * FROM dbo.Resource B WHERE B.ResourceTypeId = A.ResourceTypeId)

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
