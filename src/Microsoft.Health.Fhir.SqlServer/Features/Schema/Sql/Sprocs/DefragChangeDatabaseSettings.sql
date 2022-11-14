﻿--DROP PROCEDURE dbo.DefragChangeDatabaseSettings
GO
CREATE PROCEDURE dbo.DefragChangeDatabaseSettings @IsOn bit
WITH EXECUTE AS SELF
AS
set nocount on
DECLARE @SP varchar(100) = 'DefragChangeDatabaseSettings'
       ,@Mode varchar(200) = 'On='+convert(varchar,@IsOn)
       ,@st datetime = getUTCdate()
       ,@SQL varchar(3500) 

BEGIN TRY
  EXECUTE dbo.LogEvent @Process=@SP,@Status='Start',@Mode=@Mode

  SET @SQL = 'ALTER DATABASE CURRENT SET AUTO_UPDATE_STATISTICS '+CASE WHEN @IsOn = 1 THEN 'ON' ELSE 'OFF' END
  EXECUTE(@SQL)
  EXECUTE dbo.LogEvent @Process=@SP,@Status='Run',@Mode=@Mode,@Text=@SQL

  SET @SQL = 'ALTER DATABASE CURRENT SET AUTO_CREATE_STATISTICS '+CASE WHEN @IsOn = 1 THEN 'ON' ELSE 'OFF' END
  EXECUTE(@SQL)

  EXECUTE dbo.LogEvent @Process=@SP,@Status='End',@Mode=@Mode,@Start=@st,@Text=@SQL
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
--EXECUTE dbo.DefragChangeDatabaseSettings 1
