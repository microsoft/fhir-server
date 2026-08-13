--DROP PROCEDURE dbo.GetQueryStorePlanDiagnostics
GO
CREATE PROCEDURE dbo.GetQueryStorePlanDiagnostics @PlanId bigint
WITH EXECUTE AS 'dbo'
AS
BEGIN
    SET NOCOUNT ON

    DECLARE @SP varchar(100) = OBJECT_NAME(@@PROCID)
           ,@Mode varchar(200) = 'QueryStorePlanDiagnostics'
           ,@Start datetime = GETUTCDATE()
           ,@Rows int = 0
           ,@QueryStoreState nvarchar(60)
           ,@QueryStoreReadonlyReason bigint
           ,@AuditText nvarchar(3500)
           ,@FoundPlanId bigint
           ,@QueryId bigint
           ,@QueryHash binary(8)
           ,@QueryPlanHash binary(8)
           ,@QuerySqlText nvarchar(max)
           ,@ObjectId int
           ,@PlanGroupId bigint
           ,@EngineVersion nvarchar(128)
           ,@CompatibilityLevel smallint
           ,@IsOnlineIndexPlan bit
           ,@IsTrivialPlan bit
           ,@IsParallelPlan bit
           ,@IsForcedPlan bit
           ,@ForceFailureCount bigint
           ,@LastForceFailureReason int
           ,@LastForceFailureReasonDesc nvarchar(256)
           ,@CountCompiles bigint
           ,@InitialCompileStartTime datetimeoffset(7)
           ,@LastCompileStartTime datetimeoffset(7)
           ,@LastPlanExecutionTime datetimeoffset(7)
           ,@AverageCompileDuration float
           ,@LastCompileDuration bigint
           ,@FirstExecutionTime datetimeoffset(7)
           ,@LastRuntimeExecutionTime datetimeoffset(7)
           ,@RawQueryPlan nvarchar(max)
           ,@LocalPlanXml xml
           ,@SanitizedShowPlanXml xml
           ,@SerializedPlanXml nvarchar(max)
           ,@ParameterListCount bigint
           ,@ParameterListRemoved bigint = 0
           ,@RemainingParameterListCount bigint
           ,@ForbiddenAttributeCount bigint
           ,@SanitizationStatus varchar(32)
           ,@SanitizationErrorCode varchar(64)
           ,@SerializedResultSizeBytes bigint = 0
           ,@CaughtErrorNumber int
           ,@CaughtErrorState int

    SET @AuditText = CONCAT(
        N'OriginalLogin=', CONVERT(nvarchar(128), ORIGINAL_LOGIN()),
        N';EffectivePrincipal=', CONVERT(nvarchar(128), USER_NAME()),
        N';PlanId=', ISNULL(CONVERT(nvarchar(20), @PlanId), N'NULL'))

    BEGIN TRY
        EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start',@Text=@AuditText

        IF @PlanId IS NULL OR @PlanId <= 0
            THROW 50001, 'Plan ID must be a positive bigint.', 1

        SELECT @QueryStoreState = actual_state_desc
              ,@QueryStoreReadonlyReason = readonly_reason
        FROM sys.database_query_store_options

        SET @AuditText = CONCAT(
            @AuditText,
            N';QueryStoreState=', ISNULL(CONVERT(nvarchar(60), @QueryStoreState), N'Unknown'),
            N';QueryStoreReadonlyReason=', ISNULL(CONVERT(nvarchar(20), @QueryStoreReadonlyReason), N'NULL'))

        EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Text=@AuditText

        IF @QueryStoreState IS NULL OR @QueryStoreState NOT IN ('READ_WRITE', 'READ_ONLY')
            THROW 50002, 'Query Store is not readable.', 1

        SELECT @FoundPlanId = p.plan_id
              ,@QueryId = p.query_id
              ,@QueryHash = q.query_hash
              ,@QueryPlanHash = p.query_plan_hash
              ,@QuerySqlText = qt.query_sql_text
              ,@ObjectId = q.object_id
              ,@PlanGroupId = p.plan_group_id
              ,@EngineVersion = p.engine_version
              ,@CompatibilityLevel = p.compatibility_level
              ,@IsOnlineIndexPlan = p.is_online_index_plan
              ,@IsTrivialPlan = p.is_trivial_plan
              ,@IsParallelPlan = p.is_parallel_plan
              ,@IsForcedPlan = p.is_forced_plan
              ,@ForceFailureCount = p.force_failure_count
              ,@LastForceFailureReason = p.last_force_failure_reason
              ,@LastForceFailureReasonDesc = p.last_force_failure_reason_desc
              ,@CountCompiles = p.count_compiles
              ,@InitialCompileStartTime = p.initial_compile_start_time
              ,@LastCompileStartTime = p.last_compile_start_time
              ,@LastPlanExecutionTime = p.last_execution_time
              ,@AverageCompileDuration = p.avg_compile_duration
              ,@LastCompileDuration = p.last_compile_duration
              ,@RawQueryPlan = CONVERT(nvarchar(max), p.query_plan)
        FROM sys.query_store_plan AS p
        LEFT JOIN sys.query_store_query AS q
            ON q.query_id = p.query_id
        LEFT JOIN sys.query_store_query_text AS qt
            ON qt.query_text_id = q.query_text_id
        WHERE p.plan_id = @PlanId

        IF @FoundPlanId IS NULL
            THROW 50003, 'The requested Query Store plan was not found or is no longer retained.', 1

        SELECT @FirstExecutionTime = MIN(first_execution_time)
              ,@LastRuntimeExecutionTime = MAX(last_execution_time)
        FROM sys.query_store_runtime_stats
        WHERE plan_id = @PlanId

        IF @RawQueryPlan IS NULL
        BEGIN
            SET @SanitizationStatus = 'PlanXmlUnavailable'
            SET @SanitizationErrorCode = 'PLAN_XML_UNAVAILABLE'
        END
        ELSE
        BEGIN
            SET @LocalPlanXml = TRY_CONVERT(xml, @RawQueryPlan)

            IF @LocalPlanXml IS NULL
            BEGIN
                SET @SanitizationStatus = 'InvalidXml'
                SET @SanitizationErrorCode = 'PLAN_XML_INVALID'
            END
            ELSE
            BEGIN
                BEGIN TRY
                    SET @SanitizedShowPlanXml = @LocalPlanXml
                    SET @ParameterListCount = @SanitizedShowPlanXml.value('count(//*[local-name(.) = "ParameterList"])', 'bigint')

                    WHILE @ParameterListRemoved < @ParameterListCount
                    BEGIN
                        SET @SanitizedShowPlanXml.modify('delete (//*[local-name(.) = "ParameterList"])[1]')
                        SET @ParameterListRemoved = @ParameterListRemoved + 1
                    END

                    SET @RemainingParameterListCount = @SanitizedShowPlanXml.value('count(//*[local-name(.) = "ParameterList"])', 'bigint')
                    SET @ForbiddenAttributeCount = @SanitizedShowPlanXml.value('count(//@*[local-name(.) = "ParameterCompiledValue" or local-name(.) = "ParameterRuntimeValue"])', 'bigint')
                    SET @SerializedPlanXml = CONVERT(nvarchar(max), @SanitizedShowPlanXml)

                    IF @RemainingParameterListCount = 0
                        AND @ForbiddenAttributeCount = 0
                        AND CHARINDEX(N'PARAMETERLIST', UPPER(@SerializedPlanXml)) = 0
                        AND CHARINDEX(N'PARAMETERCOMPILEDVALUE', UPPER(@SerializedPlanXml)) = 0
                        AND CHARINDEX(N'PARAMETERRUNTIMEVALUE', UPPER(@SerializedPlanXml)) = 0
                    BEGIN
                        SET @SanitizationStatus = 'Sanitized'
                    END
                    ELSE
                    BEGIN
                        SET @SanitizedShowPlanXml = NULL
                        SET @SerializedPlanXml = NULL
                        SET @SanitizationStatus = 'VerificationFailed'
                        SET @SanitizationErrorCode = 'PLAN_XML_VERIFICATION_FAILED'
                    END
                END TRY
                BEGIN CATCH
                    SET @SanitizedShowPlanXml = NULL
                    SET @SerializedPlanXml = NULL
                    SET @SanitizationStatus = 'VerificationFailed'
                    SET @SanitizationErrorCode = 'PLAN_XML_VERIFICATION_FAILED'
                END CATCH
            END
        END

        SET @SerializedResultSizeBytes = ISNULL(DATALENGTH(@SerializedPlanXml), 0)
        SET @AuditText = CONCAT(
            @AuditText,
            N';SanitizationStatus=', ISNULL(CONVERT(nvarchar(32), @SanitizationStatus), N'NotStarted'),
            N';SanitizationErrorCode=', ISNULL(CONVERT(nvarchar(64), @SanitizationErrorCode), N'NONE'),
            N';SerializedResultSizeBytes=', CONVERT(nvarchar(20), @SerializedResultSizeBytes))

        IF @SanitizationErrorCode IS NOT NULL
            EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Text=@AuditText

        SET @Rows = 1

        SELECT @FoundPlanId AS PlanId
              ,@QueryId AS QueryId
              ,@QueryHash AS QueryHash
              ,@QueryPlanHash AS QueryPlanHash
              ,@QuerySqlText AS QuerySqlText
              ,@ObjectId AS ObjectId
              ,@PlanGroupId AS PlanGroupId
              ,@EngineVersion AS EngineVersion
              ,@CompatibilityLevel AS CompatibilityLevel
              ,@CountCompiles AS CompileCount
              ,@InitialCompileStartTime AS InitialCompileStartTime
              ,@LastCompileStartTime AS LastCompileStartTime
              ,@AverageCompileDuration AS AverageCompileDurationMicroseconds
              ,@LastCompileDuration AS LastCompileDurationMicroseconds
              ,@IsOnlineIndexPlan AS IsOnlineIndexPlan
              ,@IsTrivialPlan AS IsTrivialPlan
              ,@IsParallelPlan AS IsParallelPlan
              ,@IsForcedPlan AS IsForcedPlan
              ,@ForceFailureCount AS ForceFailureCount
              ,@LastForceFailureReason AS LastForceFailureReason
              ,@LastForceFailureReasonDesc AS LastForceFailureReasonDescription
              ,@FirstExecutionTime AS FirstExecutionTime
              ,COALESCE(@LastRuntimeExecutionTime, @LastPlanExecutionTime) AS LastExecutionTime
              ,@SanitizationStatus AS SanitizationStatus
              ,@SanitizationErrorCode AS SanitizationErrorCode
              ,@SanitizedShowPlanXml AS SanitizedShowPlanXml

        EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@Start,@Rows=@Rows,@Text=@AuditText
    END TRY
    BEGIN CATCH
        SET @CaughtErrorNumber = ERROR_NUMBER()
        SET @CaughtErrorState = ERROR_STATE()
        SET @AuditText = CONCAT(
            N'OriginalLogin=', CONVERT(nvarchar(128), ORIGINAL_LOGIN()),
            N';EffectivePrincipal=', CONVERT(nvarchar(128), USER_NAME()),
            N';PlanId=', ISNULL(CONVERT(nvarchar(20), @PlanId), N'NULL'),
            N';SanitizationStatus=', ISNULL(CONVERT(nvarchar(32), @SanitizationStatus), N'NotCompleted'),
            N';SanitizationErrorCode=PLAN_DIAGNOSTICS_FAILED',
            N';ErrorNumber=', CONVERT(nvarchar(11), @CaughtErrorNumber),
            N';ErrorState=', CONVERT(nvarchar(11), @CaughtErrorState))

        EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@Start,@Text=@AuditText
        THROW
    END CATCH
END
GO
