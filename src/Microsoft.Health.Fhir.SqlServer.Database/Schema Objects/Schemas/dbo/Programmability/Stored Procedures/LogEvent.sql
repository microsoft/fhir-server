--DROP PROCEDURE dbo.LogEvent
GO
CREATE PROCEDURE dbo.LogEvent    
   @Process         varchar(100)
  ,@Status          varchar(10)
  ,@Mode            varchar(200)   = NULL    
  ,@Action          varchar(20)    = NULL    
  ,@Target          varchar(100)   = NULL    
  ,@Rows            bigint         = NULL    
  ,@Start           datetime       = NULL
  ,@Text            nvarchar(3500) = NULL
  ,@EventID         bigint         = NULL    OUTPUT
  ,@ReRaisError     bit            = 1
  ,@Retry           int            = NULL
AS
set nocount on
DECLARE @ErrorNumber  int           = error_number()
       ,@ErrorMessage varchar(1000) = ''
       ,@TranCount    int           = @@trancount
       ,@DoWork       bit           = 0
       ,@NumberAdded  bit

IF @ErrorNumber IS NOT NULL OR @Status IN ('Warn','Error')
  SET @DoWork = 1

IF @DoWork = 0
  SET @DoWork = CASE WHEN EXISTS (SELECT * FROM dbo.Parameters WHERE Id = isnull(@Process,'') AND Char = 'LogEvent') THEN 1 ELSE 0 END

IF @DoWork = 0
  RETURN

IF @ErrorNumber IS NOT NULL 
BEGIN
  SET @ErrorMessage = convert(varchar(1000), error_message())

  IF isnull(error_procedure(),'') <> 'dbo.LogEvent' AND isnull(error_procedure(),'') <> 'LogEvent' -- dbo is present only sometimes
  BEGIN
    SET @NumberAdded = CASE WHEN len(@ErrorMessage) > 10 AND patindex('Error [1-9]%[0-9]:%',@ErrorMessage) = 1 THEN 1 ELSE 0 END
    
    SET @ErrorMessage = CASE WHEN @NumberAdded = 0 AND @Retry IS NOT NULL THEN 'Retry '+convert(varchar,@Retry)+', ' ELSE '' END
                      + @ErrorMessage
                      + ', Level '+convert(varchar,error_severity())    
                      + ', State '+convert(varchar,error_state())    
                      + CASE WHEN error_procedure() IS NOT NULL THEN ', Procedure '+error_procedure() ELSE '' END   
                      + ', Line '+convert(varchar,error_line()) 

    IF @NumberAdded = 0 SET @ErrorMessage = dbo.BuildStoreNumberAndErrorMessage(@ErrorNumber, @ErrorMessage)
  END
END

IF @TranCount > 0 AND @ErrorNumber IS NOT NULL ROLLBACK TRANSACTION

IF databasepropertyex(db_name(), 'UpdateAbility') = 'READ_WRITE'
BEGIN
  INSERT INTO dbo.EventLog    
      (    
           Process
          ,Status
          ,Mode
          ,Action
          ,Target
          ,Rows
          ,Milliseconds
          ,EventDate
          ,EventText
          ,SPID
          ,HostName
      )    
    SELECT @Process
          ,@Status
          ,@Mode
          ,@Action
          ,@Target
          ,@Rows
          ,datediff(millisecond,@Start,getUTCdate())
          ,EventDate = getUTCdate()
          ,Text = CASE 
                    WHEN @ErrorNumber IS NULL THEN @Text
                    ELSE @ErrorMessage + CASE WHEN isnull(@Text,'')<>'' THEN '. '+@Text ELSE '' END
                  END
          ,@@SPID
          ,HostName = host_name()
    
  SET @EventID = scope_identity()
END
    
-- Restore @@trancount
IF @TranCount > 0 AND @ErrorNumber IS NOT NULL BEGIN TRANSACTION

IF @ErrorNumber IS NOT NULL AND isnull(@ReRaisError,1) = 1
  RAISERROR(@ErrorMessage,18,1) -- cannot raise error level > 18
GO
