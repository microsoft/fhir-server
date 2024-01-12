﻿--DROP PROCEDURE dbo.CreateResourceSearchParamStats
GO
CREATE PROCEDURE dbo.CreateResourceSearchParamStats @Table varchar(100), @Column varchar(100), @ResourceTypeId smallint, @SearchParamId smallint
WITH EXECUTE AS 'dbo'
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(200) = 'T='+isnull(@Table,'NULL')+' C='+isnull(@Column,'NULL')+' RT='+isnull(convert(varchar,@ResourceTypeId),'NULL')+' SP='+isnull(convert(varchar,@SearchParamId),'NULL')
       ,@st datetime = getUTCdate()

BEGIN TRY
  IF @Table IS NULL OR @Column IS NULL OR @ResourceTypeId IS NULL OR @SearchParamId IS NULL
    RAISERROR('@TableName IS NULL OR @KeyColumn IS NULL OR @ResourceTypeId IS NULL OR @SearchParamId IS NULL',18,127)
  
  EXECUTE('CREATE STATISTICS ST_'+@Column+'_WHERE_ResourceTypeId_'+@ResourceTypeId+'_SearchParamId_'+@SearchParamId+' ON dbo.'+@Table+' ('+@Column+') WHERE ResourceTypeId = '+@ResourceTypeId+' AND SearchParamId = '+@SearchParamId)

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Text='Stats created'
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  IF error_number() = 1927 -- stats already exists
  BEGIN
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st
    RETURN
  END
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
--INSERT INTO Parameters (Id,Char) SELECT name,'LogEvent' FROM sys.objects WHERE type = 'p'
--SELECT TOP 100 * FROM EventLog ORDER BY EventDate DESC
