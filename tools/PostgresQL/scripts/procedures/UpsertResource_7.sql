CREATE OR REPLACE PROCEDURE UpsertResource_7(
    baseResourceSurrogateId bigint,
    resourceTypeId smallint,
    resourceId varchar(64),
    eTag int,
    allowCreate bit,
    isDeleted bit,
    keepHistory bit,
    requireETagOnUpdate bit,
    requestMethod varchar(10),
    searchParamHash varchar(64),
    rawResource bytea,
    resourceWriteClaims public.BulkResourceWriteClaimTableType_1 ,
    compartmentAssignments public.BulkCompartmentAssignmentTableType_1 ,
    referenceSearchParams public.BulkReferenceSearchParamTableType_1 ,
    tokenSearchParams public.BulkTokenSearchParamTableType_1 ,
    tokenTextSearchParams public.BulkTokenTextTableType_1 ,
    stringSearchParams public.BulkStringSearchParamTableType_2 ,
    numberSearchParams public.BulkNumberSearchParamTableType_1 ,
    quantitySearchParams public.BulkQuantitySearchParamTableType_1 ,
    uriSearchParams public.BulkUriSearchParamTableType_1 ,
    dateTimeSearchParms public.BulkDateTimeSearchParamTableType_2 ,
    referenceTokenCompositeSearchParams public.BulkReferenceTokenCompositeSearchParamTableType_1 ,
    tokenTokenCompositeSearchParams public.BulkTokenTokenCompositeSearchParamTableType_1 ,
    tokenDateTimeCompositeSearchParams public.BulkTokenDateTimeCompositeSearchParamTableType_1 ,
    tokenQuantityCompositeSearchParams public.BulkTokenQuantityCompositeSearchParamTableType_1 ,
    tokenStringCompositeSearchParams public.BulkTokenStringCompositeSearchParamTableType_1 ,
    tokenNumberNumberCompositeSearchParams public.BulkTokenNumberNumberCompositeSearchParamTableType_1 ,
    isResourceChangeCaptureEnabled bit,
    comparedVersion int
)
LANGUAGE plpgsql
as $$
DECLARE
	previousResourceSurrogateId bigint;
	previousVersion bigint;
	previousIsDeleted bit;
	version int;
	resourceSurrogateId bigint;
	isRawResourceMetaSet bit;
	
BEGIN
	SELECT previousResourceSurrogateId = ResourceSurrogateId,
	       previousVersion = Version,
		   previousIsDeleted = IsDeleted
    FROM   Resource
	WHERE  ResourceTypeId = resourceTypeId
		   AND ResourceId = resourceId
		   AND IsHistory = 0 :: bit;
	
	IF previousResourceSurrogateId IS NULL THEN
		version := 1;
	ELSE
		
		IF isDeleted = 0 :: bit THEN

			IF comparedVersion IS NULL OR comparedVersion <> previousVersion THEN
				RAISE EXCEPTION 'Resource has been recently updated or added, please compare the resource content in code for any duplicate updates';
			END IF;

		END IF;
		version := previousVersion + 1;
		IF keepHistory = 1 :: bit THEN

			UPDATE resource
			SET    IsHistory = 1 :: bit
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			UPDATE CompartmentAssignment
			SET    IsHistory = 1 :: bit
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			UPDATE ReferenceSearchParam
			SET    IsHistory = 1 :: bit
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			UPDATE TokenSearchParam
			SET    IsHistory = 1 :: bit
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			UPDATE TokenText
			SET    IsHistory = 1 :: bit
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			UPDATE StringSearchParam
			SET    IsHistory = 1 :: bit
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			UPDATE UriSearchParam
			SET    IsHistory = 1 :: bit
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			UPDATE NumberSearchParam
			SET    IsHistory = 1 :: bit
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			UPDATE QuantitySearchParam
			SET    IsHistory = 1 :: bit
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			UPDATE DateTimeSearchParam
			SET    IsHistory = 1 :: bit
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			UPDATE ReferenceTokenCompositeSearchParam
			SET    IsHistory = 1 :: bit
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			UPDATE TokenTokenCompositeSearchParam
			SET    IsHistory = 1 :: bit
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			UPDATE TokenDateTimeCompositeSearchParam
			SET    IsHistory = 1 :: bit
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			UPDATE TokenQuantityCompositeSearchParam
			SET    IsHistory = 1 :: bit
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			UPDATE TokenStringCompositeSearchParam
			SET    IsHistory = 1 :: bit
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			UPDATE TokenNumberNumberCompositeSearchParam
			SET    IsHistory = 1 :: bit
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;

		ELSE

			DELETE FROM Resource
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			DELETE FROM ResourceWriteClaim
			WHERE  ResourceSurrogateId = previousResourceSurrogateId;
			DELETE FROM CompartmentAssignment
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			DELETE FROM ReferenceSearchParam
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			DELETE FROM TokenSearchParam
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			DELETE FROM TokenText
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			DELETE FROM StringSearchParam
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			DELETE FROM UriSearchParam
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			DELETE FROM NumberSearchParam
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			DELETE FROM QuantitySearchParam
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			DELETE FROM DateTimeSearchParam
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			DELETE FROM ReferenceTokenCompositeSearchParam
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			DELETE FROM TokenTokenCompositeSearchParam
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			DELETE FROM TokenDateTimeCompositeSearchParam
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			DELETE FROM TokenQuantityCompositeSearchParam
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			DELETE FROM TokenStringCompositeSearchParam
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;
			DELETE FROM TokenNumberNumberCompositeSearchParam
			WHERE  ResourceTypeId = resourceTypeId
				   AND ResourceSurrogateId = previousResourceSurrogateId;

		END IF;
		
	END IF;
	
	resourceSurrogateId := baseResourceSurrogateId + nextval('ResourceSurrogateIdUniquifierSequence');
	IF version = 1 :: bit THEN
		isRawResourceMetaSet := 1 :: bit;
	ELSE
		isRawResourceMetaSet := 0 :: bit;
	END IF;
	
    INSERT INTO Resource (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash)
    VALUES(resourceTypeId, resourceId, version, 0 :: bit, resourceSurrogateId, isDeleted, requestMethod, rawResource, isRawResourceMetaSet, searchParamHash);
	
	INSERT INTO ResourceWriteClaim (ResourceSurrogateId, ClaimTypeId, ClaimValue)
	SELECT resourceSurrogateId,
		   ClaimTypeId,
		   ClaimValue
	FROM   resourceWriteClaims;
	INSERT INTO CompartmentAssignment (ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId, IsHistory)
	SELECT DISTINCT resourceTypeId,
					resourceSurrogateId,
					CompartmentTypeId,
					ReferenceResourceId,
					0 :: bit
	FROM   compartmentAssignments;
	INSERT INTO ReferenceSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, IsHistory)
	SELECT DISTINCT resourceTypeId,
					resourceSurrogateId,
					SearchParamId,
					BaseUri,
					ReferenceResourceTypeId,
					ReferenceResourceId,
					ReferenceResourceVersion,
					0 :: bit
	FROM   referenceSearchParams;
	INSERT INTO TokenSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, IsHistory)
	SELECT DISTINCT resourceTypeId,
					resourceSurrogateId,
					SearchParamId,
					SystemId,
					Code,
					0 :: bit
	FROM   tokenSearchParams;
	INSERT INTO TokenText (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, IsHistory)
	SELECT DISTINCT resourceTypeId,
					resourceSurrogateId,
					SearchParamId,
					Text,
					0 :: bit
	FROM   tokenTextSearchParams;
	INSERT INTO StringSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsHistory, IsMin, IsMax)
	SELECT DISTINCT resourceTypeId,
					resourceSurrogateId,
					SearchParamId,
					Text,
					TextOverflow,
					0 :: bit,
					IsMin,
					IsMax
	FROM   stringSearchParams;
	INSERT INTO UriSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri, IsHistory)
	SELECT DISTINCT resourceTypeId,
					resourceSurrogateId,
					SearchParamId,
					Uri,
					0 :: bit
	FROM   uriSearchParams;
	INSERT INTO NumberSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, IsHistory)
	SELECT DISTINCT resourceTypeId,
					resourceSurrogateId,
					SearchParamId,
					SingleValue,
					LowValue,
					HighValue,
					0 :: bit
	FROM   numberSearchParams;
	INSERT INTO QuantitySearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, IsHistory)
	SELECT DISTINCT resourceTypeId,
					resourceSurrogateId,
					SearchParamId,
					SystemId,
					QuantityCodeId,
					SingleValue,
					LowValue,
					HighValue,
					0 :: bit
	FROM   quantitySearchParams;
	INSERT INTO DateTimeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsHistory, IsMin, IsMax)
	SELECT DISTINCT resourceTypeId,
					resourceSurrogateId,
					SearchParamId,
					StartDateTime,
					EndDateTime,
					IsLongerThanADay,
					0 :: bit,
					IsMin,
					IsMax
	FROM   dateTimeSearchParms;
	INSERT INTO ReferenceTokenCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, IsHistory)
	SELECT DISTINCT resourceTypeId,
					resourceSurrogateId,
					SearchParamId,
					BaseUri1,
					ReferenceResourceTypeId1,
					ReferenceResourceId1,
					ReferenceResourceVersion1,
					SystemId2,
					Code2,
					0 :: bit
	FROM   referenceTokenCompositeSearchParams;
	INSERT INTO TokenTokenCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SystemId2, Code2, IsHistory)
	SELECT DISTINCT resourceTypeId,
					resourceSurrogateId,
					SearchParamId,
					SystemId1,
					Code1,
					SystemId2,
					Code2,
					0 :: bit
	FROM   tokenTokenCompositeSearchParams;
	INSERT INTO TokenDateTimeCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, StartDateTime2, EndDateTime2, IsLongerThanADay2, IsHistory)
	SELECT DISTINCT resourceTypeId,
					resourceSurrogateId,
					SearchParamId,
					SystemId1,
					Code1,
					StartDateTime2,
					EndDateTime2,
					IsLongerThanADay2,
					0 :: bit
	FROM   tokenDateTimeCompositeSearchParams;
	INSERT INTO TokenQuantityCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, IsHistory)
	SELECT DISTINCT resourceTypeId,
					resourceSurrogateId,
					SearchParamId,
					SystemId1,
					Code1,
					SingleValue2,
					SystemId2,
					QuantityCodeId2,
					LowValue2,
					HighValue2,
					0 :: bit
	FROM   tokenQuantityCompositeSearchParams;
	INSERT INTO TokenStringCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, Text2, TextOverflow2, IsHistory)
	SELECT DISTINCT resourceTypeId,
					resourceSurrogateId,
					SearchParamId,
					SystemId1,
					Code1,
					Text2,
					TextOverflow2,
					0 :: bit
	FROM   tokenStringCompositeSearchParams;
	INSERT INTO TokenNumberNumberCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, IsHistory)
	SELECT DISTINCT resourceTypeId,
					resourceSurrogateId,
					SearchParamId,
					SystemId1,
					Code1,
					SingleValue2,
					LowValue2,
					HighValue2,
					SingleValue3,
					LowValue3,
					HighValue3,
					HasRange,
					0 :: bit
	FROM   tokenNumberNumberCompositeSearchParams;
	SELECT version;

END;
$$;