--DROP PROCEDURE dbo.MergeResourcesDeleteInvisibleHistory
GO
CREATE PROCEDURE dbo.MergeResourcesDeleteInvisibleHistory @TransactionId bigint, @AffectedRows int = NULL OUT
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = 'T='+convert(varchar,@TransactionId)
       ,@st datetime
       ,@Rows int
       ,@DeletedIdMap int

SET @AffectedRows = 0

Retry:
BEGIN TRY 
  DECLARE @Ids TABLE (ResourceTypeId smallint NOT NULL, ResourceIdInt bigint NOT NULL)
  
  BEGIN TRANSACTION

  SET @st = getUTCdate()
  DELETE FROM A
    OUTPUT deleted.ResourceTypeId, deleted.ResourceIdInt INTO @Ids 
    FROM dbo.Resource A
    WHERE HistoryTransactionId = @TransactionId
  SET @Rows = @@rowcount
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Target='Resource',@Action='Delete',@Start=@st,@Rows=@Rows
  SET @AffectedRows += @Rows

  SET @st = getUTCdate()
  IF @Rows > 0
  BEGIN
    -- remove referenced in resources
    DELETE FROM A FROM @Ids A WHERE EXISTS (SELECT * FROM dbo.CurrentResources B WHERE B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt)
    SET @Rows -= @@rowcount
    IF @Rows > 0
    BEGIN
      -- remove referenced in reference search params
      DELETE FROM A FROM @Ids A WHERE EXISTS (SELECT * FROM dbo.ResourceReferenceSearchParams B WHERE B.ReferenceResourceTypeId = A.ResourceTypeId AND B.ReferenceResourceIdInt = A.ResourceIdInt)
      SET @Rows -= @@rowcount
      IF @Rows > 0
      BEGIN
        -- delete from id map
        DELETE FROM B FROM @Ids A INNER LOOP JOIN dbo.ResourceIdIntMap B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceIdInt = A.ResourceIdInt
        SET @DeletedIdMap = @@rowcount
      END
    END
  END
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Target='ResourceIdIntMap',@Action='Delete',@Start=@st,@Rows=@DeletedIdMap

  COMMIT TRANSACTION
  
  SET @st = getUTCdate()
  UPDATE dbo.Resource SET TransactionId = NULL WHERE TransactionId = @TransactionId
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Target='Resource',@Action='Update',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  IF @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error'
  IF error_number() = 547 AND error_message() LIKE '%DELETE%' -- reference violation on DELETE
  BEGIN
    DELETE FROM @Ids
    GOTO Retry
  END
  ELSE
    THROW
END CATCH
GO
