CREATE PROCEDURE dbo.GetResourceSearchParamStats @Table varchar(100) = NULL, @ResourceTypeId smallint = NULL, @SearchParamId smallint = NULL
WITH EXECUTE AS 'dbo'
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(200) = 'T='+isnull(@Table,'NULL')+' RT='+isnull(convert(varchar,@ResourceTypeId),'NULL')+' SP='+isnull(convert(varchar,@SearchParamId),'NULL')
       ,@st datetime = getUTCdate()

BEGIN TRY
  SELECT T.name
        ,S.name
    FROM sys.stats S
         JOIN sys.tables T ON T.object_id = S.object_id
    WHERE T.name LIKE '%SearchParam' AND T.name <> 'SearchParam'
      AND S.name LIKE 'ST[_]%'
      AND (T.name LIKE @Table OR @Table IS NULL)
      AND (S.name LIKE '%ResourceTypeId[_]'+convert(varchar,@ResourceTypeId)+'[_]%' OR @ResourceTypeId IS NULL)
      AND (S.name LIKE '%SearchParamId[_]'+convert(varchar,@SearchParamId) OR @SearchParamId IS NULL)

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
--INSERT INTO Parameters (Id,Char) SELECT name,'LogEvent' FROM sys.objects WHERE type = 'p'
--SELECT TOP 100 * FROM EventLog ORDER BY EventDate DESC
