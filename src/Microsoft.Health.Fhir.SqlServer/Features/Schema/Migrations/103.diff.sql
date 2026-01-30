ALTER PROCEDURE dbo.GetSearchParamStatuses @LastUpdated datetimeoffset(7) = NULL OUT
AS
set nocount on
DECLARE @SP varchar(100) = 'GetSearchParamStatuses'
       ,@Mode varchar(100) = ''
       ,@st datetime = getUTCdate()
       ,@msg varchar(100)

BEGIN TRY
  SET @LastUpdated = (SELECT max(LastUpdated) FROM dbo.SearchParam)

  SET @msg = 'LastUpdated='+substring(convert(varchar,@LastUpdated),1,23)

  SELECT SearchParamId, Uri, Status, LastUpdated, IsPartiallySupported FROM dbo.SearchParam

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount,@Action='Select',@Target='SearchParam',@Text=@msg
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
INSERT INTO dbo.Parameters (Id,Char) SELECT 'GetSearchParamStatuses', 'LogEvent'
GO
CREATE OR ALTER PROCEDURE dbo.MergeSearchParams @SearchParams dbo.SearchParamList READONLY
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(200) = 'Uri.Status='+(SELECT TOP 1 Uri+'.'+Status COLLATE Latin1_General_100_CS_AS FROM @SearchParams)
       ,@st datetime = getUTCdate()
       ,@LastUpdated datetimeoffset(7) = sysdatetimeoffset()
       ,@msg varchar(4000)
       ,@Rows int

DECLARE @SummaryOfChanges TABLE (Uri varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL, Operation varchar(20) NOT NULL)

BEGIN TRY
  SET TRANSACTION ISOLATION LEVEL SERIALIZABLE

  BEGIN TRANSACTION
  
  -- Check for concurrency conflicts first using LastUpdated
  SELECT @msg = string_agg(S.Uri, ', ') 
    FROM @SearchParams I JOIN dbo.SearchParam S ON S.Uri = I.Uri
    WHERE I.LastUpdated != S.LastUpdated
  IF @msg IS NOT NULL
  BEGIN
    SET @msg = concat('Optimistic concurrency conflict detected for search parameters: ', @msg) 
    ROLLBACK TRANSACTION;
    THROW 50001, @msg, 1
  END

  MERGE INTO dbo.SearchParam S
    USING @SearchParams I ON I.Uri = S.Uri
    WHEN MATCHED THEN 
      UPDATE 
        SET Status = I.Status
           ,LastUpdated = @LastUpdated
           ,IsPartiallySupported = I.IsPartiallySupported
    WHEN NOT MATCHED BY TARGET THEN 
      INSERT   (  Uri,   Status,  LastUpdated,   IsPartiallySupported) 
        VALUES (I.Uri, I.Status, @LastUpdated, I.IsPartiallySupported)
    OUTPUT I.Uri, $action INTO @SummaryOfChanges;
  SET @Rows = @@rowcount

  SELECT S.SearchParamId
        ,S.Uri
        ,S.LastUpdated
    FROM dbo.SearchParam S JOIN @SummaryOfChanges C ON C.Uri = S.Uri
    WHERE C.Operation = 'INSERT'
  SET @msg = 'LastUpdated='+substring(convert(varchar,@LastUpdated),1,23)+' INSERT='+convert(varchar,@@rowcount)

  COMMIT TRANSACTION

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Action='Merge',@Rows=@Rows,@Text=@msg
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION;
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
INSERT INTO Parameters (Id,Char) SELECT 'MergeSearchParams','LogEvent'
GO
IF object_id('UpsertSearchParams') IS NOT NULL DROP PROCEDURE UpsertSearchParams
GO
IF EXISTS (SELECT * FROM systypes WHERE name = 'SearchParamTableType_2') DROP TYPE dbo.SearchParamTableType_2
GO
IF EXISTS (SELECT * FROM systypes WHERE name = 'BulkReindexResourceTableType_1') DROP TYPE dbo.BulkReindexResourceTableType_1
GO
INSERT INTO Parameters (Id,Char) SELECT 'EnqueueJobs','LogEvent'
GO
