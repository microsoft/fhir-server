--DROP PROCEDURE dbo.MergeResources 
GO
CREATE PROCEDURE dbo.MergeResources
  @SimpleInsert bit = 1
 ,@Resources ResourceList READONLY
 ,@ReferenceSearchParams ReferenceSearchParamList READONLY
 ,@TokenSearchParams TokenSearchParamList READONLY
 ,@CompartmentAssignments CompartmentAssignmentList READONLY
 ,@TokenTexts TokenTextList READONLY
 ,@DateTimeSearchParams DateTimeSearchParamList READONLY
 ,@TokenQuantityCompositeSearchParams TokenQuantityCompositeSearchParamList READONLY
 ,@QuantitySearchParams QuantitySearchParamList READONLY
 ,@StringSearchParams StringSearchParamList READONLY
 ,@TokenTokenCompositeSearchParams TokenTokenCompositeSearchParamList READONLY
 ,@TokenStringCompositeSearchParams TokenStringCompositeSearchParamList READONLY
 ,@AffectedRows int = NULL OUT
AS
set nocount on
DECLARE @st datetime = getUTCdate()
       ,@SP varchar(100) = 'MergeResources'
       ,@ResourceTypeId smallint
       ,@TransactionId bigint
       ,@InputRows int
       ,@OldRows int = 0
       ,@MaxSequence bigint
       ,@Offset bigint
       ,@DummyTop bigint = 9223372036854775807

-- There should be single transaction 
SELECT @TransactionId = max(TransactionId), @ResourceTypeId = max(ResourceTypeId), @InputRows = count(*) FROM @Resources -- validate whether "all resources have same RT" assumption is acually needed

DECLARE @Mode varchar(100) = 'T='+convert(varchar,@TransactionId)+' RT='+convert(varchar,@ResourceTypeId)+' Rows='+convert(varchar,@InputRows)+' SI='+convert(varchar,@SimpleInsert)

SET @AffectedRows = 0

BEGIN TRY
  DECLARE @TrueResources AS TABLE
    (
       TransactionId        bigint         NOT NULL
      ,ShardletId           tinyint        NOT NULL
      ,Sequence             smallint       NOT NULL
      ,ResourceTypeId       smallint       NOT NULL
      ,ResourceId           varchar(64)    COLLATE Latin1_General_100_CS_AS NOT NULL -- Collation here should not matter as we don't do any ResourceId comparisons with @TrueResources
      ,Version              int            NOT NULL
      ,RequestMethod        varchar(10)    NULL
      ,SearchParamHash      varchar(64)    NULL

      PRIMARY KEY (TransactionId, ShardletId, Sequence)
    )

  IF @SimpleInsert = 1 -- avoid join by resource id and apply version logic later
    INSERT INTO @TrueResources
        (
             TransactionId
            ,ShardletId
            ,Sequence
            ,ResourceTypeId
            ,ResourceId
            ,Version
            ,RequestMethod
            ,SearchParamHash
        )
      SELECT TransactionId
            ,ShardletId
            ,Sequence
            ,ResourceTypeId
            ,ResourceId
            ,Version = 0
            ,RequestMethod
            ,SearchParamHash
        FROM @Resources
  ELSE
    INSERT INTO @TrueResources
        (
             TransactionId
            ,ShardletId
            ,Sequence
            ,ResourceTypeId
            ,ResourceId
            ,Version
            ,RequestMethod
            ,SearchParamHash
        )
      SELECT A.TransactionId
            ,A.ShardletId
            ,A.Sequence
            ,A.ResourceTypeId
            ,A.ResourceId
            ,Version = isnull(B.Version + 1, 0)
            ,A.RequestMethod
            ,A.SearchParamHash
        FROM (SELECT TOP (@DummyTop) * FROM @Resources) A
             CROSS APPLY (SELECT TOP 1 Version FROM dbo.Resource B WHERE B.ResourceTypeId = @ResourceTypeId AND B.ResourceId = A.ResourceId AND B.IsHistory = 0 ORDER BY Version DESC) B
        OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))

  INSERT INTO dbo.Resource
         ( ResourceTypeId,TransactionId,ShardletId,Sequence,ResourceId,Version,IsHistory,IsDeleted,RequestMethod,SearchParamHash)
    SELECT ResourceTypeId,TransactionId,ShardletId,Sequence,ResourceId,Version,        0,        0,RequestMethod,SearchParamHash
      FROM (SELECT TOP (@DummyTop) * FROM @TrueResources) A
      WHERE NOT EXISTS (SELECT * FROM dbo.Resource B WHERE B.ResourceTypeId = @ResourceTypeId AND B.TransactionId = A.TransactionId AND B.ShardletId = A.ShardletId AND B.Sequence = A.Sequence)
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
  SET @AffectedRows = @AffectedRows + @@rowcount

  INSERT INTO dbo.ReferenceSearchParam
          (ResourceTypeId,TransactionId,ShardletId,Sequence,SearchParamId,BaseUri,ReferenceResourceTypeId,ReferenceResourceId,ReferenceResourceVersion,IsHistory)
    SELECT ResourceTypeId,TransactionId,ShardletId,Sequence,SearchParamId,BaseUri,ReferenceResourceTypeId,ReferenceResourceId,ReferenceResourceVersion,        0
      FROM (SELECT TOP (@DummyTop) * FROM @ReferenceSearchParams) A
      WHERE NOT EXISTS (SELECT * FROM dbo.Resource B WHERE B.ResourceTypeId = @ResourceTypeId AND B.TransactionId = A.TransactionId AND B.ShardletId = A.ShardletId AND B.Sequence = A.Sequence)
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
  SET @AffectedRows = @AffectedRows + @@rowcount

  INSERT INTO dbo.TokenSearchParam
        (  ResourceTypeId,TransactionId,ShardletId,Sequence,SearchParamId,SystemId,Code,IsHistory)
    SELECT ResourceTypeId,TransactionId,ShardletId,Sequence,SearchParamId,SystemId,Code,        0
      FROM (SELECT TOP (@DummyTop) * FROM @TokenSearchParams) A
      WHERE NOT EXISTS (SELECT * FROM dbo.ReferenceSearchParam B WHERE B.ResourceTypeId = @ResourceTypeId AND B.TransactionId = A.TransactionId AND B.ShardletId = A.ShardletId AND B.Sequence = A.Sequence)
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
  SET @AffectedRows = @AffectedRows + @@rowcount

  INSERT INTO dbo.CompartmentAssignment
          (ResourceTypeId,TransactionId,ShardletId,Sequence,CompartmentTypeId,ReferenceResourceId,IsHistory)
    SELECT ResourceTypeId,TransactionId,ShardletId,Sequence,CompartmentTypeId,ReferenceResourceId,        0
      FROM (SELECT TOP (@DummyTop) * FROM @CompartmentAssignments) A
      WHERE NOT EXISTS (SELECT * FROM dbo.CompartmentAssignment B WHERE B.ResourceTypeId = @ResourceTypeId AND B.TransactionId = A.TransactionId AND B.ShardletId = A.ShardletId AND B.Sequence = A.Sequence)
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
  SET @AffectedRows = @AffectedRows + @@rowcount

  INSERT INTO dbo.TokenText
          (ResourceTypeId,TransactionId,ShardletId,Sequence,SearchParamId,Text,IsHistory)
    SELECT ResourceTypeId,TransactionId,ShardletId,Sequence,SearchParamId,Text,        0
      FROM (SELECT TOP (@DummyTop) * FROM @TokenTexts) A
      WHERE NOT EXISTS (SELECT * FROM dbo.TokenText B WHERE B.ResourceTypeId = @ResourceTypeId AND B.TransactionId = A.TransactionId AND B.ShardletId = A.ShardletId AND B.Sequence = A.Sequence)
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
  SET @AffectedRows = @AffectedRows + @@rowcount

  INSERT INTO dbo.DateTimeSearchParam
          (ResourceTypeId,TransactionId,ShardletId,Sequence,SearchParamId,StartDateTime,EndDateTime,IsLongerThanADay,IsHistory,IsMin,IsMax)
    SELECT ResourceTypeId,TransactionId,ShardletId,Sequence,SearchParamId,StartDateTime,EndDateTime,IsLongerThanADay,        0,IsMin,IsMax
      FROM (SELECT TOP (@DummyTop) * FROM @DateTimeSearchParams) A
      WHERE NOT EXISTS (SELECT * FROM dbo.DateTimeSearchParam B WHERE B.ResourceTypeId = @ResourceTypeId AND B.TransactionId = A.TransactionId AND B.ShardletId = A.ShardletId AND B.Sequence = A.Sequence)
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
  SET @AffectedRows = @AffectedRows + @@rowcount

  INSERT INTO dbo.TokenQuantityCompositeSearchParam
          (ResourceTypeId,TransactionId,ShardletId,Sequence,SearchParamId,SystemId1,Code1,SystemId2,QuantityCodeId2,SingleValue2,LowValue2,HighValue2,IsHistory)
    SELECT ResourceTypeId,TransactionId,ShardletId,Sequence,SearchParamId,SystemId1,Code1,SystemId2,QuantityCodeId2,SingleValue2,LowValue2,HighValue2,        0
      FROM (SELECT TOP (@DummyTop) * FROM @TokenQuantityCompositeSearchParams) A
      WHERE NOT EXISTS (SELECT * FROM dbo.TokenQuantityCompositeSearchParam B WHERE B.ResourceTypeId = @ResourceTypeId AND B.TransactionId = A.TransactionId AND B.ShardletId = A.ShardletId AND B.Sequence = A.Sequence)
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
  SET @AffectedRows = @AffectedRows + @@rowcount

  INSERT INTO dbo.QuantitySearchParam
          (ResourceTypeId,TransactionId,ShardletId,Sequence,SearchParamId,SystemId,QuantityCodeId,SingleValue,LowValue,HighValue,IsHistory)
    SELECT ResourceTypeId,TransactionId,ShardletId,Sequence,SearchParamId,SystemId,QuantityCodeId,SingleValue,LowValue,HighValue,        0
      FROM (SELECT TOP (@DummyTop) * FROM @QuantitySearchParams) A
      WHERE NOT EXISTS (SELECT * FROM dbo.QuantitySearchParam B WHERE B.ResourceTypeId = @ResourceTypeId AND B.TransactionId = A.TransactionId AND B.ShardletId = A.ShardletId AND B.Sequence = A.Sequence)
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
  SET @AffectedRows = @AffectedRows + @@rowcount

  INSERT INTO dbo.StringSearchParam
          (ResourceTypeId,TransactionId,ShardletId,Sequence,SearchParamId,Text,TextOverflow,IsHistory,IsMin,IsMax)
    SELECT ResourceTypeId,TransactionId,ShardletId,Sequence,SearchParamId,Text,TextOverflow,        0,IsMin,IsMax
      FROM (SELECT TOP (@DummyTop) * FROM @StringSearchParams) A
      WHERE NOT EXISTS (SELECT * FROM dbo.Resource B WHERE B.ResourceTypeId = @ResourceTypeId AND B.TransactionId = A.TransactionId AND B.ShardletId = A.ShardletId AND B.Sequence = A.Sequence)
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
  SET @AffectedRows = @AffectedRows + @@rowcount

  INSERT INTO dbo.TokenTokenCompositeSearchParam
          (ResourceTypeId,TransactionId,ShardletId,Sequence,SearchParamId,SystemId1,Code1,SystemId2,Code2,IsHistory)
    SELECT ResourceTypeId,TransactionId,ShardletId,Sequence,SearchParamId,SystemId1,Code1,SystemId2,Code2,        0
      FROM (SELECT TOP (@DummyTop) * FROM @TokenTokenCompositeSearchParams) A
      WHERE NOT EXISTS (SELECT * FROM dbo.StringSearchParam B WHERE B.ResourceTypeId = @ResourceTypeId AND B.TransactionId = A.TransactionId AND B.ShardletId = A.ShardletId AND B.Sequence = A.Sequence)
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
  SET @AffectedRows = @AffectedRows + @@rowcount

  INSERT INTO dbo.TokenStringCompositeSearchParam
          (ResourceTypeId,TransactionId,ShardletId,Sequence,SearchParamId,SystemId1,Code1,Text2,TextOverflow2,IsHistory)
    SELECT ResourceTypeId,TransactionId,ShardletId,Sequence,SearchParamId,SystemId1,Code1,Text2,TextOverflow2,        0
      FROM (SELECT TOP (@DummyTop) * FROM @TokenStringCompositeSearchParams) A
      WHERE NOT EXISTS (SELECT * FROM dbo.TokenStringCompositeSearchParam B WHERE B.ResourceTypeId = @ResourceTypeId AND B.TransactionId = A.TransactionId AND B.ShardletId = A.ShardletId AND B.Sequence = A.Sequence)
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
  SET @AffectedRows = @AffectedRows + @@rowcount

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@AffectedRows,@Text=@OldRows
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
