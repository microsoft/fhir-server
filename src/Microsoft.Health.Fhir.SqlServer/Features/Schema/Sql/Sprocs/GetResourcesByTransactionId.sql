--DROP PROCEDURE dbo.GetResourcesByTransactionId
GO
CREATE PROCEDURE dbo.GetResourcesByTransactionId @TransactionId bigint, @IncludeHistory bit = 0, @ReturnResourceKeysOnly bit = 0
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = 'T='+convert(varchar,@TransactionId)+' H='+convert(varchar,@IncludeHistory)
       ,@st datetime = getUTCdate()

BEGIN TRY
  IF @ReturnResourceKeysOnly = 0
    SELECT ResourceTypeId
          ,ResourceId
          ,ResourceSurrogateId
          ,Version
          ,IsDeleted
          ,IsHistory
          ,RawResource
          ,IsRawResourceMetaSet
          ,SearchParamHash
          ,RequestMethod
          ,FileId
          ,OffsetInFile
          ,ResourceLength
      FROM dbo.Resource
      WHERE TransactionId = @TransactionId AND (IsHistory = 0 OR @IncludeHistory = 1)
      OPTION (MAXDOP 1)
  ELSE
    SELECT ResourceTypeId
          ,ResourceId
          ,ResourceSurrogateId
          ,Version
          ,IsDeleted
      FROM dbo.Resource
      WHERE TransactionId = @TransactionId AND (IsHistory = 0 OR @IncludeHistory = 1)
      OPTION (MAXDOP 1)

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
--DECLARE @Tran bigint = (SELECT TOP 1 SurrogateIdRangeFirstValue FROM Transactions WHERE IsVisible = 1 ORDER BY 1 DESC)
--EXECUTE GetResourcesByTransactionId @Tran
