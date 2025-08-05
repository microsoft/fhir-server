DECLARE @ErrorMessage NVARCHAR(200) = 'Your reindex job has been canceled during an upgrade. Please resubmit a new one.';

UPDATE dbo.ReindexJob
SET 
    Status = 'Canceled',
	RawJobRecord = JSON_MODIFY(RawJobRecord, '$.error', @ErrorMessage)
WHERE Status NOT IN ('Completed', 'Failed', 'Canceled');
GO
ALTER PROCEDURE dbo.UpdateResourceSearchParams
    @FailedResources int = 0 OUT
   ,@Resources dbo.ResourceList READONLY
   ,@ResourceWriteClaims dbo.ResourceWriteClaimList READONLY
   ,@ReferenceSearchParams dbo.ReferenceSearchParamList READONLY
   ,@TokenSearchParams dbo.TokenSearchParamList READONLY
   ,@TokenTexts dbo.TokenTextList READONLY
   ,@StringSearchParams dbo.StringSearchParamList READONLY
   ,@UriSearchParams dbo.UriSearchParamList READONLY
   ,@NumberSearchParams dbo.NumberSearchParamList READONLY
   ,@QuantitySearchParams dbo.QuantitySearchParamList READONLY
   ,@DateTimeSearchParams dbo.DateTimeSearchParamList READONLY
   ,@ReferenceTokenCompositeSearchParams dbo.ReferenceTokenCompositeSearchParamList READONLY
   ,@TokenTokenCompositeSearchParams dbo.TokenTokenCompositeSearchParamList READONLY
   ,@TokenDateTimeCompositeSearchParams dbo.TokenDateTimeCompositeSearchParamList READONLY
   ,@TokenQuantityCompositeSearchParams dbo.TokenQuantityCompositeSearchParamList READONLY
   ,@TokenStringCompositeSearchParams dbo.TokenStringCompositeSearchParamList READONLY
   ,@TokenNumberNumberCompositeSearchParams dbo.TokenNumberNumberCompositeSearchParamList READONLY
AS
set nocount on
DECLARE @st datetime = getUTCdate()
       ,@SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(200) = isnull((SELECT 'RT=['+convert(varchar,min(ResourceTypeId))+','+convert(varchar,max(ResourceTypeId))+'] Sur=['+convert(varchar,min(ResourceSurrogateId))+','+convert(varchar,max(ResourceSurrogateId))+'] V='+convert(varchar,max(Version))+' Rows='+convert(varchar,count(*)) FROM @Resources),'Input=Empty')
       ,@Rows int
       ,@ReferenceSearchParamsCurrent dbo.ReferenceSearchParamList
       ,@ReferenceSearchParamsDelete dbo.ReferenceSearchParamList
       ,@ReferenceSearchParamsInsert dbo.ReferenceSearchParamList
       ,@TokenSearchParamsCurrent dbo.TokenSearchParamList
       ,@TokenSearchParamsDelete dbo.TokenSearchParamList
       ,@TokenSearchParamsInsert dbo.TokenSearchParamList
       ,@TokenTextsCurrent dbo.TokenTextList
       ,@TokenTextsDelete dbo.TokenTextList
       ,@TokenTextsInsert dbo.TokenTextList
       ,@StringSearchParamsCurrent dbo.StringSearchParamList
       ,@StringSearchParamsDelete dbo.StringSearchParamList
       ,@StringSearchParamsInsert dbo.StringSearchParamList
       ,@UriSearchParamsCurrent dbo.UriSearchParamList
       ,@UriSearchParamsDelete dbo.UriSearchParamList
       ,@UriSearchParamsInsert dbo.UriSearchParamList
       ,@NumberSearchParamsCurrent dbo.NumberSearchParamList
       ,@NumberSearchParamsDelete dbo.NumberSearchParamList
       ,@NumberSearchParamsInsert dbo.NumberSearchParamList
       ,@QuantitySearchParamsCurrent dbo.QuantitySearchParamList
       ,@QuantitySearchParamsDelete dbo.QuantitySearchParamList
       ,@QuantitySearchParamsInsert dbo.QuantitySearchParamList
       ,@DateTimeSearchParamsCurrent dbo.DateTimeSearchParamList
       ,@DateTimeSearchParamsDelete dbo.DateTimeSearchParamList
       ,@DateTimeSearchParamsInsert dbo.DateTimeSearchParamList
       ,@ReferenceTokenCompositeSearchParamsCurrent dbo.ReferenceTokenCompositeSearchParamList
       ,@ReferenceTokenCompositeSearchParamsDelete dbo.ReferenceTokenCompositeSearchParamList
       ,@ReferenceTokenCompositeSearchParamsInsert dbo.ReferenceTokenCompositeSearchParamList
       ,@TokenTokenCompositeSearchParamsCurrent dbo.TokenTokenCompositeSearchParamList
       ,@TokenTokenCompositeSearchParamsDelete dbo.TokenTokenCompositeSearchParamList
       ,@TokenTokenCompositeSearchParamsInsert dbo.TokenTokenCompositeSearchParamList
       ,@TokenDateTimeCompositeSearchParamsCurrent dbo.TokenDateTimeCompositeSearchParamList
       ,@TokenDateTimeCompositeSearchParamsDelete dbo.TokenDateTimeCompositeSearchParamList
       ,@TokenDateTimeCompositeSearchParamsInsert dbo.TokenDateTimeCompositeSearchParamList
       ,@TokenQuantityCompositeSearchParamsCurrent dbo.TokenQuantityCompositeSearchParamList
       ,@TokenQuantityCompositeSearchParamsDelete dbo.TokenQuantityCompositeSearchParamList
       ,@TokenQuantityCompositeSearchParamsInsert dbo.TokenQuantityCompositeSearchParamList
       ,@TokenStringCompositeSearchParamsCurrent dbo.TokenStringCompositeSearchParamList
       ,@TokenStringCompositeSearchParamsDelete dbo.TokenStringCompositeSearchParamList
       ,@TokenStringCompositeSearchParamsInsert dbo.TokenStringCompositeSearchParamList
       ,@TokenNumberNumberCompositeSearchParamsCurrent dbo.TokenNumberNumberCompositeSearchParamList
       ,@TokenNumberNumberCompositeSearchParamsDelete dbo.TokenNumberNumberCompositeSearchParamList
       ,@TokenNumberNumberCompositeSearchParamsInsert dbo.TokenNumberNumberCompositeSearchParamList
       ,@ResourceWriteClaimsCurrent dbo.ResourceWriteClaimList
       ,@ResourceWriteClaimsDelete dbo.ResourceWriteClaimList
       ,@ResourceWriteClaimsInsert dbo.ResourceWriteClaimList

BEGIN TRY
  DECLARE @Ids TABLE (ResourceTypeId smallint NOT NULL, ResourceSurrogateId bigint NOT NULL)

  BEGIN TRANSACTION

  -- Update the search parameter hash value in the main resource table
  UPDATE B
    SET SearchParamHash = A.SearchParamHash
    OUTPUT deleted.ResourceTypeId, deleted.ResourceSurrogateId INTO @Ids 
    FROM @Resources A JOIN dbo.Resource B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
    WHERE B.IsHistory = 0
  SET @Rows = @@rowcount

  -- ResourceWriteClaim - Incremental update pattern
  INSERT INTO @ResourceWriteClaimsCurrent
         ( ResourceSurrogateId, ClaimTypeId, ClaimValue )
    SELECT A.ResourceSurrogateId, ClaimTypeId, ClaimValue
      FROM dbo.ResourceWriteClaim A
           JOIN @Ids B ON B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @ResourceWriteClaimsDelete
         ( ResourceSurrogateId, ClaimTypeId, ClaimValue )
    SELECT ResourceSurrogateId, ClaimTypeId, ClaimValue
      FROM @ResourceWriteClaimsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @ResourceWriteClaims B 
                  WHERE B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.ClaimTypeId = A.ClaimTypeId
                    AND B.ClaimValue = A.ClaimValue
              )

  DELETE FROM A
    FROM dbo.ResourceWriteClaim A
    WHERE EXISTS 
            (SELECT * 
               FROM @ResourceWriteClaimsDelete B 
                WHERE B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.ClaimTypeId = A.ClaimTypeId
                  AND B.ClaimValue = A.ClaimValue
            )

  INSERT INTO @ResourceWriteClaimsInsert
         ( ResourceSurrogateId, ClaimTypeId, ClaimValue )
    SELECT ResourceSurrogateId, ClaimTypeId, ClaimValue
      FROM @ResourceWriteClaims A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @ResourceWriteClaimsCurrent B 
                  WHERE B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.ClaimTypeId = A.ClaimTypeId
                    AND B.ClaimValue = A.ClaimValue
              )

  -- ReferenceSearchParam - Incremental update pattern
  INSERT INTO @ReferenceSearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion
      FROM dbo.ReferenceSearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @ReferenceSearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion
      FROM @ReferenceSearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @ReferenceSearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.BaseUri = A.BaseUri OR B.BaseUri IS NULL AND A.BaseUri IS NULL)
                    AND (B.ReferenceResourceTypeId = A.ReferenceResourceTypeId OR B.ReferenceResourceTypeId IS NULL AND A.ReferenceResourceTypeId IS NULL)
                    AND B.ReferenceResourceId = A.ReferenceResourceId
              )

  DELETE FROM A
    FROM dbo.ReferenceSearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @ReferenceSearchParamsDelete B 
               WHERE B.ResourceTypeId = A.ResourceTypeId 
                 AND B.ResourceSurrogateId = A.ResourceSurrogateId
                 AND B.SearchParamId = A.SearchParamId
                 AND (B.BaseUri = A.BaseUri OR B.BaseUri IS NULL AND A.BaseUri IS NULL)
                 AND (B.ReferenceResourceTypeId = A.ReferenceResourceTypeId OR B.ReferenceResourceTypeId IS NULL AND A.ReferenceResourceTypeId IS NULL)
                 AND B.ReferenceResourceId = A.ReferenceResourceId
             )

  INSERT INTO @ReferenceSearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion
      FROM @ReferenceSearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @ReferenceSearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.BaseUri = A.BaseUri OR B.BaseUri IS NULL AND A.BaseUri IS NULL)
                    AND (B.ReferenceResourceTypeId = A.ReferenceResourceTypeId OR B.ReferenceResourceTypeId IS NULL AND A.ReferenceResourceTypeId IS NULL)
                    AND B.ReferenceResourceId = A.ReferenceResourceId
              )

  -- TokenSearchParam - Incremental update pattern
  INSERT INTO @TokenSearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow
      FROM dbo.TokenSearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @TokenSearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow
      FROM @TokenSearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenSearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId = A.SystemId OR B.SystemId IS NULL AND A.SystemId IS NULL)
                    AND B.Code = A.Code
                    AND (B.CodeOverflow = A.CodeOverflow OR B.CodeOverflow IS NULL AND A.CodeOverflow IS NULL)
              )

  DELETE FROM A
    FROM dbo.TokenSearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @TokenSearchParamsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND (B.SystemId = A.SystemId OR B.SystemId IS NULL AND A.SystemId IS NULL)
                  AND B.Code = A.Code
                  AND (B.CodeOverflow = A.CodeOverflow OR B.CodeOverflow IS NULL AND A.CodeOverflow IS NULL)
            )

  INSERT INTO @TokenSearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow
      FROM @TokenSearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenSearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId = A.SystemId OR B.SystemId IS NULL AND A.SystemId IS NULL)
                    AND B.Code = A.Code
                    AND (B.CodeOverflow = A.CodeOverflow OR B.CodeOverflow IS NULL AND A.CodeOverflow IS NULL)
              )

  -- TokenStringCompositeSearchParam - Incremental update pattern
  INSERT INTO @TokenStringCompositeSearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2 )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2
      FROM dbo.TokenStringCompositeSearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @TokenStringCompositeSearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2
      FROM @TokenStringCompositeSearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenStringCompositeSearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                    AND B.Code1 = A.Code1
                    AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                    AND B.Text2 = A.Text2
                    AND (B.TextOverflow2 = A.TextOverflow2 OR B.TextOverflow2 IS NULL AND A.TextOverflow2 IS NULL)
              )

  DELETE FROM A
    FROM dbo.TokenStringCompositeSearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @TokenStringCompositeSearchParamsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                  AND B.Code1 = A.Code1
                  AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                  AND B.Text2 COLLATE Latin1_General_100_CI_AI_SC = A.Text2
                  AND (B.TextOverflow2 COLLATE Latin1_General_100_CI_AI_SC = A.TextOverflow2 OR B.TextOverflow2 IS NULL AND A.TextOverflow2 IS NULL)
            )

  INSERT INTO @TokenStringCompositeSearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2
      FROM @TokenStringCompositeSearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenStringCompositeSearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                    AND B.Code1 = A.Code1
                    AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                    AND B.Text2 = A.Text2
                    AND (B.TextOverflow2 = A.TextOverflow2 OR B.TextOverflow2 IS NULL AND A.TextOverflow2 IS NULL)
              )
			  
  -- TokenText - Incremental update pattern
  INSERT INTO @TokenTextsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, Text
      FROM dbo.TokenText A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @TokenTextsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text
      FROM @TokenTextsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenTexts B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND B.Text = A.Text
              )

  DELETE FROM A
    FROM dbo.TokenText A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @TokenTextsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND B.Text = A.Text
            )

  INSERT INTO @TokenTextsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text
      FROM @TokenTexts A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenTextsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND B.Text = A.Text
              )

  -- StringSearchParam - Incremental update pattern
  INSERT INTO @StringSearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax
      FROM dbo.StringSearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @StringSearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax
      FROM @StringSearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @StringSearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND B.Text = A.Text
                    AND (B.TextOverflow = A.TextOverflow OR B.TextOverflow IS NULL AND A.TextOverflow IS NULL)
                    AND B.IsMin = A.IsMin
                    AND B.IsMax = A.IsMax
              )

  DELETE FROM A
    FROM dbo.StringSearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @StringSearchParamsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND B.Text = A.Text
                  AND (B.TextOverflow = A.TextOverflow OR B.TextOverflow IS NULL AND A.TextOverflow IS NULL)
                  AND B.IsMin = A.IsMin
                  AND B.IsMax = A.IsMax
            )

  INSERT INTO @StringSearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax
      FROM @StringSearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @StringSearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND B.Text = A.Text
                    AND (B.TextOverflow = A.TextOverflow OR B.TextOverflow IS NULL AND A.TextOverflow IS NULL)
                    AND B.IsMin = A.IsMin
                    AND B.IsMax = A.IsMax
              )

  -- UriSearchParam - Incremental update pattern
  INSERT INTO @UriSearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, Uri
      FROM dbo.UriSearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @UriSearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri
      FROM @UriSearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @UriSearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND B.Uri = A.Uri
              )

  DELETE FROM A
    FROM dbo.UriSearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @UriSearchParamsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND B.Uri = A.Uri
            )

  INSERT INTO @UriSearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri
      FROM @UriSearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @UriSearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND B.Uri = A.Uri
              )
              
  -- NumberSearchParam - Incremental update pattern
  INSERT INTO @NumberSearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue
      FROM dbo.NumberSearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @NumberSearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue
      FROM @NumberSearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @NumberSearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SingleValue = A.SingleValue OR B.SingleValue IS NULL AND A.SingleValue IS NULL)
                    AND (B.LowValue = A.LowValue OR B.LowValue IS NULL AND A.LowValue IS NULL)
                    AND (B.HighValue = A.HighValue OR B.HighValue IS NULL AND A.HighValue IS NULL)
              )

  DELETE FROM A
    FROM dbo.NumberSearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @NumberSearchParamsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND (B.SingleValue = A.SingleValue OR B.SingleValue IS NULL AND A.SingleValue IS NULL)
                  AND (B.LowValue = A.LowValue OR B.LowValue IS NULL AND A.LowValue IS NULL)
                  AND (B.HighValue = A.HighValue OR B.HighValue IS NULL AND A.HighValue IS NULL)
            )

  INSERT INTO @NumberSearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue
      FROM @NumberSearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @NumberSearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SingleValue = A.SingleValue OR B.SingleValue IS NULL AND A.SingleValue IS NULL)
                    AND (B.LowValue = A.LowValue OR B.LowValue IS NULL AND A.LowValue IS NULL)
                    AND (B.HighValue = A.HighValue OR B.HighValue IS NULL AND A.HighValue IS NULL)
              )
              
  -- QuantitySearchParam - Incremental update pattern
  INSERT INTO @QuantitySearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue
      FROM dbo.QuantitySearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @QuantitySearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue
      FROM @QuantitySearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @QuantitySearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId = A.SystemId OR B.SystemId IS NULL AND A.SystemId IS NULL)
                    AND (B.QuantityCodeId = A.QuantityCodeId OR B.QuantityCodeId IS NULL AND A.QuantityCodeId IS NULL)
                    AND (B.SingleValue = A.SingleValue OR B.SingleValue IS NULL AND A.SingleValue IS NULL)
                    AND (B.LowValue = A.LowValue OR B.LowValue IS NULL AND A.LowValue IS NULL)
                    AND (B.HighValue = A.HighValue OR B.HighValue IS NULL AND A.HighValue IS NULL)
              )

  DELETE FROM A
    FROM dbo.QuantitySearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @QuantitySearchParamsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND (B.SystemId = A.SystemId OR B.SystemId IS NULL AND A.SystemId IS NULL)
                  AND (B.QuantityCodeId = A.QuantityCodeId OR B.QuantityCodeId IS NULL AND A.QuantityCodeId IS NULL)
                  AND (B.SingleValue = A.SingleValue OR B.SingleValue IS NULL AND A.SingleValue IS NULL)
                  AND (B.LowValue = A.LowValue OR B.LowValue IS NULL AND A.LowValue IS NULL)
                  AND (B.HighValue = A.HighValue OR B.HighValue IS NULL AND A.HighValue IS NULL)
            )

  INSERT INTO @QuantitySearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue
      FROM @QuantitySearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @QuantitySearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId = A.SystemId OR B.SystemId IS NULL AND A.SystemId IS NULL)
                    AND (B.QuantityCodeId = A.QuantityCodeId OR B.QuantityCodeId IS NULL AND A.QuantityCodeId IS NULL)
                    AND (B.SingleValue = A.SingleValue OR B.SingleValue IS NULL AND A.SingleValue IS NULL)
                    AND (B.LowValue = A.LowValue OR B.LowValue IS NULL AND A.LowValue IS NULL)
                    AND (B.HighValue = A.HighValue OR B.HighValue IS NULL AND A.HighValue IS NULL)
              )

  -- DateTimeSearchParam - Incremental update pattern
  INSERT INTO @DateTimeSearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax
      FROM dbo.DateTimeSearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @DateTimeSearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax
      FROM @DateTimeSearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @DateTimeSearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND B.StartDateTime = A.StartDateTime
                    AND B.EndDateTime = A.EndDateTime
                    AND B.IsLongerThanADay = A.IsLongerThanADay
                    AND B.IsMin = A.IsMin
                    AND B.IsMax = A.IsMax
              )

  DELETE FROM A
    FROM dbo.DateTimeSearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @DateTimeSearchParamsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND B.StartDateTime = A.StartDateTime
                  AND B.EndDateTime = A.EndDateTime
                  AND B.IsLongerThanADay = A.IsLongerThanADay
                  AND B.IsMin = A.IsMin
                  AND B.IsMax = A.IsMax
            )

  INSERT INTO @DateTimeSearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax
      FROM @DateTimeSearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @DateTimeSearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND B.StartDateTime = A.StartDateTime
                    AND B.EndDateTime = A.EndDateTime
                    AND B.IsLongerThanADay = A.IsLongerThanADay
                    AND B.IsMin = A.IsMin
                    AND B.IsMax = A.IsMax
              )

  -- ReferenceTokenCompositeSearchParam - Incremental update pattern
  INSERT INTO @ReferenceTokenCompositeSearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2 )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2
      FROM dbo.ReferenceTokenCompositeSearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @ReferenceTokenCompositeSearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2
      FROM @ReferenceTokenCompositeSearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @ReferenceTokenCompositeSearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.BaseUri1 = A.BaseUri1 OR B.BaseUri1 IS NULL AND A.BaseUri1 IS NULL)
                    AND (B.ReferenceResourceTypeId1 = A.ReferenceResourceTypeId1 OR B.ReferenceResourceTypeId1 IS NULL AND A.ReferenceResourceTypeId1 IS NULL)
                    AND B.ReferenceResourceId1 = A.ReferenceResourceId1
                    AND (B.SystemId2 = A.SystemId2 OR B.SystemId2 IS NULL AND A.SystemId2 IS NULL)
                    AND B.Code2 = A.Code2
                    AND (B.CodeOverflow2 = A.CodeOverflow2 OR B.CodeOverflow2 IS NULL AND A.CodeOverflow2 IS NULL)
              )

  DELETE FROM A
    FROM dbo.ReferenceTokenCompositeSearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @ReferenceTokenCompositeSearchParamsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND (B.BaseUri1 = A.BaseUri1 OR B.BaseUri1 IS NULL AND A.BaseUri1 IS NULL)
                  AND (B.ReferenceResourceTypeId1 = A.ReferenceResourceTypeId1 OR B.ReferenceResourceTypeId1 IS NULL AND A.ReferenceResourceTypeId1 IS NULL)
                  AND B.ReferenceResourceId1 = A.ReferenceResourceId1
                  AND (B.SystemId2 = A.SystemId2 OR B.SystemId2 IS NULL AND A.SystemId2 IS NULL)
                  AND B.Code2 = A.Code2
                  AND (B.CodeOverflow2 = A.CodeOverflow2 OR B.CodeOverflow2 IS NULL AND A.CodeOverflow2 IS NULL)
            )

  INSERT INTO @ReferenceTokenCompositeSearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2
      FROM @ReferenceTokenCompositeSearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @ReferenceTokenCompositeSearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.BaseUri1 = A.BaseUri1 OR B.BaseUri1 IS NULL AND A.BaseUri1 IS NULL)
                    AND (B.ReferenceResourceTypeId1 = A.ReferenceResourceTypeId1 OR B.ReferenceResourceTypeId1 IS NULL AND A.ReferenceResourceTypeId1 IS NULL)
                    AND B.ReferenceResourceId1 = A.ReferenceResourceId1
                    AND (B.SystemId2 = A.SystemId2 OR B.SystemId2 IS NULL AND A.SystemId2 IS NULL)
                    AND B.Code2 = A.Code2
                    AND (B.CodeOverflow2 = A.CodeOverflow2 OR B.CodeOverflow2 IS NULL AND A.CodeOverflow2 IS NULL)
              )

  -- TokenTokenCompositeSearchParam - Incremental update pattern
  INSERT INTO @TokenTokenCompositeSearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2 )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2
      FROM dbo.TokenTokenCompositeSearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @TokenTokenCompositeSearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2
      FROM @TokenTokenCompositeSearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenTokenCompositeSearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                    AND B.Code1 = A.Code1
                    AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                    AND (B.SystemId2 = A.SystemId2 OR B.SystemId2 IS NULL AND A.SystemId2 IS NULL)
                    AND B.Code2 = A.Code2
                    AND (B.CodeOverflow2 = A.CodeOverflow2 OR B.CodeOverflow2 IS NULL AND A.CodeOverflow2 IS NULL)
              )

  DELETE FROM A
    FROM dbo.TokenTokenCompositeSearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @TokenTokenCompositeSearchParamsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                  AND B.Code1 = A.Code1
                  AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                  AND (B.SystemId2 = A.SystemId2 OR B.SystemId2 IS NULL AND A.SystemId2 IS NULL)
                  AND B.Code2 = A.Code2
                  AND (B.CodeOverflow2 = A.CodeOverflow2 OR B.CodeOverflow2 IS NULL AND A.CodeOverflow2 IS NULL)
            )

  INSERT INTO @TokenTokenCompositeSearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2
      FROM @TokenTokenCompositeSearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenTokenCompositeSearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                    AND B.Code1 = A.Code1
                    AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                    AND (B.SystemId2 = A.SystemId2 OR B.SystemId2 IS NULL AND A.SystemId2 IS NULL)
                    AND B.Code2 = A.Code2
                    AND (B.CodeOverflow2 = A.CodeOverflow2 OR B.CodeOverflow2 IS NULL AND A.CodeOverflow2 IS NULL)
              )
              
  -- TokenDateTimeCompositeSearchParam - Incremental update pattern
  INSERT INTO @TokenDateTimeCompositeSearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2 )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2
      FROM dbo.TokenDateTimeCompositeSearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @TokenDateTimeCompositeSearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2
      FROM @TokenDateTimeCompositeSearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenDateTimeCompositeSearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                    AND B.Code1 = A.Code1
                    AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                    AND B.StartDateTime2 = A.StartDateTime2
                    AND B.EndDateTime2 = A.EndDateTime2
                    AND B.IsLongerThanADay2 = A.IsLongerThanADay2
              )

  DELETE FROM A
    FROM dbo.TokenDateTimeCompositeSearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @TokenDateTimeCompositeSearchParamsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                  AND B.Code1 = A.Code1
                  AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                  AND B.StartDateTime2 = A.StartDateTime2
                  AND B.EndDateTime2 = A.EndDateTime2
                  AND B.IsLongerThanADay2 = A.IsLongerThanADay2
            )

  INSERT INTO @TokenDateTimeCompositeSearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2
      FROM @TokenDateTimeCompositeSearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenDateTimeCompositeSearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                    AND B.Code1 = A.Code1
                    AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                    AND B.StartDateTime2 = A.StartDateTime2
                    AND B.EndDateTime2 = A.EndDateTime2
                    AND B.IsLongerThanADay2 = A.IsLongerThanADay2
              )

  -- TokenQuantityCompositeSearchParam - Incremental update pattern
  INSERT INTO @TokenQuantityCompositeSearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2 )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2
      FROM dbo.TokenQuantityCompositeSearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @TokenQuantityCompositeSearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2
      FROM @TokenQuantityCompositeSearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenQuantityCompositeSearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                    AND B.Code1 = A.Code1
                    AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                    AND (B.SingleValue2 = A.SingleValue2 OR B.SingleValue2 IS NULL AND A.SingleValue2 IS NULL)
                    AND (B.SystemId2 = A.SystemId2 OR B.SystemId2 IS NULL AND A.SystemId2 IS NULL)
                    AND (B.QuantityCodeId2 = A.QuantityCodeId2 OR B.QuantityCodeId2 IS NULL AND A.QuantityCodeId2 IS NULL)
                    AND (B.LowValue2 = A.LowValue2 OR B.LowValue2 IS NULL AND A.LowValue2 IS NULL)
                    AND (B.HighValue2 = A.HighValue2 OR B.HighValue2 IS NULL AND A.HighValue2 IS NULL)
              )

  DELETE FROM A
    FROM dbo.TokenQuantityCompositeSearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @TokenQuantityCompositeSearchParamsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                  AND B.Code1 = A.Code1
                  AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                  AND (B.SingleValue2 = A.SingleValue2 OR B.SingleValue2 IS NULL AND A.SingleValue2 IS NULL)
                  AND (B.SystemId2 = A.SystemId2 OR B.SystemId2 IS NULL AND A.SystemId2 IS NULL)
                  AND (B.QuantityCodeId2 = A.QuantityCodeId2 OR B.QuantityCodeId2 IS NULL AND A.QuantityCodeId2 IS NULL)
                  AND (B.LowValue2 = A.LowValue2 OR B.LowValue2 IS NULL AND A.LowValue2 IS NULL)
                  AND (B.HighValue2 = A.HighValue2 OR B.HighValue2 IS NULL AND A.HighValue2 IS NULL)
            )

  INSERT INTO @TokenQuantityCompositeSearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2
      FROM @TokenQuantityCompositeSearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenQuantityCompositeSearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                    AND B.Code1 = A.Code1
                    AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                    AND (B.SingleValue2 = A.SingleValue2 OR B.SingleValue2 IS NULL AND A.SingleValue2 IS NULL)
                    AND (B.SystemId2 = A.SystemId2 OR B.SystemId2 IS NULL AND A.SystemId2 IS NULL)
                    AND (B.QuantityCodeId2 = A.QuantityCodeId2 OR B.QuantityCodeId2 IS NULL AND A.QuantityCodeId2 IS NULL)
                    AND (B.LowValue2 = A.LowValue2 OR B.LowValue2 IS NULL AND A.LowValue2 IS NULL)
                    AND (B.HighValue2 = A.HighValue2 OR B.HighValue2 IS NULL AND A.HighValue2 IS NULL)
              )
  
    -- TokenNumberNumberCompositeSearchParam - Incremental update pattern
  INSERT INTO @TokenNumberNumberCompositeSearchParamsCurrent
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange
      FROM dbo.TokenNumberNumberCompositeSearchParam A
           JOIN @Ids B ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId
  
  INSERT INTO @TokenNumberNumberCompositeSearchParamsDelete
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange
      FROM @TokenNumberNumberCompositeSearchParamsCurrent A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenNumberNumberCompositeSearchParams B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                    AND B.Code1 = A.Code1
                    AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                    AND (B.SingleValue2 = A.SingleValue2 OR B.SingleValue2 IS NULL AND A.SingleValue2 IS NULL)
                    AND (B.LowValue2 = A.LowValue2 OR B.LowValue2 IS NULL AND A.LowValue2 IS NULL)
                    AND (B.HighValue2 = A.HighValue2 OR B.HighValue2 IS NULL AND A.HighValue2 IS NULL)
                    AND (B.SingleValue3 = A.SingleValue3 OR B.SingleValue3 IS NULL AND A.SingleValue3 IS NULL)
                    AND (B.LowValue3 = A.LowValue3 OR B.LowValue3 IS NULL AND A.LowValue3 IS NULL)
                    AND (B.HighValue3 = A.HighValue3 OR B.HighValue3 IS NULL AND A.HighValue3 IS NULL)
                    AND B.HasRange = A.HasRange
              )

  DELETE FROM A
    FROM dbo.TokenNumberNumberCompositeSearchParam A WITH (INDEX = 1)
    WHERE EXISTS 
            (SELECT * 
               FROM @TokenNumberNumberCompositeSearchParamsDelete B 
                WHERE B.ResourceTypeId = A.ResourceTypeId 
                  AND B.ResourceSurrogateId = A.ResourceSurrogateId
                  AND B.SearchParamId = A.SearchParamId
                  AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                  AND B.Code1 = A.Code1
                  AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                  AND (B.SingleValue2 = A.SingleValue2 OR B.SingleValue2 IS NULL AND A.SingleValue2 IS NULL)
                  AND (B.LowValue2 = A.LowValue2 OR B.LowValue2 IS NULL AND A.LowValue2 IS NULL)
                  AND (B.HighValue2 = A.HighValue2 OR B.HighValue2 IS NULL AND A.HighValue2 IS NULL)
                  AND (B.SingleValue3 = A.SingleValue3 OR B.SingleValue3 IS NULL AND A.SingleValue3 IS NULL)
                  AND (B.LowValue3 = A.LowValue3 OR B.LowValue3 IS NULL AND A.LowValue3 IS NULL)
                  AND (B.HighValue3 = A.HighValue3 OR B.HighValue3 IS NULL AND A.HighValue3 IS NULL)
                  AND B.HasRange = A.HasRange
            )

  INSERT INTO @TokenNumberNumberCompositeSearchParamsInsert
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange
      FROM @TokenNumberNumberCompositeSearchParams A
      WHERE NOT EXISTS 
              (SELECT * 
                 FROM @TokenNumberNumberCompositeSearchParamsCurrent B 
                  WHERE B.ResourceTypeId = A.ResourceTypeId 
                    AND B.ResourceSurrogateId = A.ResourceSurrogateId
                    AND B.SearchParamId = A.SearchParamId
                    AND (B.SystemId1 = A.SystemId1 OR B.SystemId1 IS NULL AND A.SystemId1 IS NULL)
                    AND B.Code1 = A.Code1
                    AND (B.CodeOverflow1 = A.CodeOverflow1 OR B.CodeOverflow1 IS NULL AND A.CodeOverflow1 IS NULL)
                    AND (B.SingleValue2 = A.SingleValue2 OR B.SingleValue2 IS NULL AND A.SingleValue2 IS NULL)
                    AND (B.LowValue2 = A.LowValue2 OR B.LowValue2 IS NULL AND A.LowValue2 IS NULL)
                    AND (B.HighValue2 = A.HighValue2 OR B.HighValue2 IS NULL AND A.HighValue2 IS NULL)
                    AND (B.SingleValue3 = A.SingleValue3 OR B.SingleValue3 IS NULL AND A.SingleValue3 IS NULL)
                    AND (B.LowValue3 = A.LowValue3 OR B.LowValue3 IS NULL AND A.LowValue3 IS NULL)
                    AND (B.HighValue3 = A.HighValue3 OR B.HighValue3 IS NULL AND A.HighValue3 IS NULL)
                    AND B.HasRange = A.HasRange
              )

  -- Next, insert all the new search params.
  INSERT INTO dbo.ResourceWriteClaim 
         ( ResourceSurrogateId, ClaimTypeId, ClaimValue )
    SELECT ResourceSurrogateId, ClaimTypeId, ClaimValue
      FROM @ResourceWriteClaimsInsert

  INSERT INTO dbo.ReferenceSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion
      FROM @ReferenceSearchParamsInsert

          INSERT INTO dbo.TokenSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow
      FROM @TokenSearchParamsInsert

  INSERT INTO dbo.TokenStringCompositeSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2
      FROM @TokenStringCompositeSearchParamsInsert
  
  INSERT INTO dbo.TokenText 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text
      FROM @TokenTextsInsert

  INSERT INTO dbo.StringSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax
      FROM @StringSearchParamsInsert

  INSERT INTO dbo.UriSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri
      FROM @UriSearchParamsInsert

  INSERT INTO dbo.NumberSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue
      FROM @NumberSearchParamsInsert

  INSERT INTO dbo.QuantitySearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue
      FROM @QuantitySearchParamsInsert

  INSERT INTO dbo.DateTimeSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsMin, IsMax
      FROM @DateTimeSearchParamsInsert

  INSERT INTO dbo.ReferenceTokenCompositeSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2
      FROM @ReferenceTokenCompositeSearchParamsInsert

  INSERT INTO dbo.TokenTokenCompositeSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2
      FROM @TokenTokenCompositeSearchParamsInsert

  INSERT INTO dbo.TokenDateTimeCompositeSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2
      FROM @TokenDateTimeCompositeSearchParamsInsert
      
  INSERT INTO dbo.TokenQuantityCompositeSearchParam
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2 )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2
      FROM @TokenQuantityCompositeSearchParamsInsert

  INSERT INTO dbo.TokenNumberNumberCompositeSearchParam 
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange )
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange
      FROM @TokenNumberNumberCompositeSearchParamsInsert

  COMMIT TRANSACTION

  SET @FailedResources = (SELECT count(*) FROM @Resources) - @Rows

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
ALTER PROCEDURE dbo.GetActiveJobs
@QueueType TINYINT, @GroupId BIGINT=NULL, @ReturnParentOnly BIT=0
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'GetActiveJobs', @Mode AS VARCHAR (100) = 'Q=' + isnull(CONVERT (VARCHAR, @QueueType), 'NULL') + ' G=' + isnull(CONVERT (VARCHAR, @GroupId), 'NULL') + ' R=' + CONVERT (VARCHAR, @ReturnParentOnly), @st AS DATETIME = getUTCdate(), @JobIds AS BigintList, @PartitionId AS TINYINT, @MaxPartitions AS TINYINT = 16, @LookedAtPartitions AS TINYINT = 0, @Rows AS INT = 0;
BEGIN TRY
    SET @PartitionId = @MaxPartitions * rand();
    WHILE @LookedAtPartitions < @MaxPartitions
        BEGIN
            IF @GroupId IS NULL
                INSERT INTO @JobIds
                SELECT JobId
                FROM   dbo.JobQueue
                WHERE  PartitionId = @PartitionId
                       AND QueueType = @QueueType
                       AND Status IN (0, 1);
            ELSE
                INSERT INTO @JobIds
                SELECT JobId
                FROM   dbo.JobQueue
                WHERE  PartitionId = @PartitionId
                       AND QueueType = @QueueType
                       AND GroupId = @GroupId
                       AND Status IN (0, 1);
            SET @Rows += @@rowcount;
            SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END;
            SET @LookedAtPartitions += 1;
        END
    IF @Rows > 0
        BEGIN
            IF @ReturnParentOnly = 1
                BEGIN
                    DECLARE @TopGroupId AS BIGINT;
                    SELECT   TOP 1 @TopGroupId = GroupId
                    FROM     dbo.JobQueue
                    WHERE    JobId IN (SELECT Id
                                       FROM   @JobIds)
                    ORDER BY GroupId DESC;
                    DELETE @JobIds
                    WHERE  Id NOT IN (SELECT JobId
                                      FROM   dbo.JobQueue
                                      WHERE  JobId = @TopGroupId);
                END
            EXECUTE dbo.GetJobs @QueueType = @QueueType, @JobIds = @JobIds;
        END
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @Rows;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH