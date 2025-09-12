/*************************************************************
    Add GetSearchParamMaxLastUpdated stored procedure
**************************************************************/

-- Create stored procedure for getting max LastUpdated timestamp from SearchParam table
CREATE OR ALTER PROCEDURE dbo.GetSearchParamMaxLastUpdated
AS
SET NOCOUNT ON

DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(200) = 'SearchParam MaxLastUpdated Query'
       ,@st datetime = getUTCdate()
       ,@MaxLastUpdated datetimeoffset(7)

BEGIN TRY
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start',@Start=@st

    -- Get the maximum LastUpdated timestamp from SearchParam table
    SELECT @MaxLastUpdated = MAX(LastUpdated) 
    FROM dbo.SearchParam

    -- Return the result
    SELECT @MaxLastUpdated AS MaxLastUpdated

    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@ROWCOUNT
END TRY
BEGIN CATCH
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
    THROW
END CATCH
GO
