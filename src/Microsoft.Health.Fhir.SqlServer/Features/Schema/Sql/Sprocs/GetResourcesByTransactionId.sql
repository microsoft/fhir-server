--DROP PROCEDURE dbo.GetResourcesByTransactionId
GO
CREATE PROCEDURE dbo.GetResourcesByTransactionId @TransactionId bigint, @IncludeHistory bit = 0
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = 'T='+convert(varchar,@TransactionId)+' H='+convert(varchar,@IncludeHistory)
       ,@st datetime = getUTCdate()
       ,@DummyTop bigint = 9223372036854775807
       ,@TypeId smallint

BEGIN TRY
  DECLARE @Types TABLE (TypeId smallint PRIMARY KEY, Name varchar(100))
  INSERT INTO @Types EXECUTE dbo.GetUsedResourceTypes

  DECLARE @Keys TABLE (TypeId smallint, SurrogateId bigint PRIMARY KEY (TypeId, SurrogateId))
  WHILE EXISTS (SELECT * FROM @Types)
  BEGIN
    SET @TypeId = (SELECT TOP 1 TypeId FROM @Types ORDER BY TypeId)

    INSERT INTO @Keys SELECT @TypeId, ResourceSurrogateId FROM dbo.Resource WHERE ResourceTypeId = @TypeId AND Transactiond = @TransactionId

    DELETE FROM @Types WHERE TypeId = @TypeId
  END

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
    FROM (SELECT TOP (@DummyTop) * FROM @Keys) A
         JOIN dbo.Resource B ON ResourceTypeId = TypeId AND ResourceSurrogateId = SurrogateId
    WHERE IsHistory = 0 OR @IncludeHistory = 1
    OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))

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
