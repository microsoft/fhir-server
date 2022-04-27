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
  ,@EventId         bigint         = NULL    OUTPUT
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
  SET @ErrorMessage = CASE WHEN @Retry IS NOT NULL THEN 'Retry '+convert(varchar,@Retry)+', ' ELSE '' END
                    + 'Error '+convert(varchar,error_number())+': '
                    + convert(varchar(1000), error_message())
                    + ', Level '+convert(varchar,error_severity())    
                    + ', State '+convert(varchar,error_state())    
                    + CASE WHEN error_procedure() IS NOT NULL THEN ', Procedure '+error_procedure() ELSE '' END   
                    + ', Line '+convert(varchar,error_line()) 

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
    
  SET @EventId = scope_identity()
END
    
-- Restore @@trancount
IF @TranCount > 0 AND @ErrorNumber IS NOT NULL BEGIN TRANSACTION
GO
