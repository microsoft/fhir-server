-- PROCEDURE: public.upsertresource_3(bigint, smallint, character varying, integer, bit, bit, bit, bit, character varying, character varying, bytea, bulkresourcewriteclaimtabletype_1[], bulktokentexttabletype_2[], bit, integer)

-- DROP PROCEDURE IF EXISTS public.upsertresource_3(bigint, smallint, character varying, integer, bit, bit, bit, bit, character varying, character varying, bytea, bulkresourcewriteclaimtabletype_1[], bulktokentexttabletype_2[], bit, integer);

CREATE OR REPLACE PROCEDURE public.upsertresource_3(
	IN baseresourcesurrogateid bigint,
	IN restypeid smallint,
	IN resid character varying,
	IN etag integer,
	IN allowcreate bit,
	IN isdeleted bit,
	IN keephistory bit,
	IN requireetagonupdate bit,
	IN requestmethod character varying,
	IN searchparamhash character varying,
	IN rawresource bytea,
	IN resourcewriteclaims bulkresourcewriteclaimtabletype_1[],
	IN tokentextsearchparams bulktokentexttabletype_2[],
	IN isresourcechangecaptureenabled bit,
	IN comparedversion integer)
LANGUAGE 'plpgsql'
AS $BODY$
DECLARE
	previousResourceSurrogateId bigint;
	previousVersion bigint;
	previousIsDeleted bit;
	version int;
	resSurrogateId bigint;
	isRawResourceMetaSet bit;
	x bulkresourcewriteclaimtabletype_1;
	y bulktokentexttabletype_2;
	
BEGIN
    IF isDeleted = 1 :: bit THEN
        DELETE FROM Resource
		WHERE  ResourceTypeId = restypeid
				AND ResourceId = resid;
		DELETE FROM TokenText
		WHERE  ResourceTypeId = restypeid
				AND ResourceId = resid;
    ELSE
        PERFORM previousResourceSurrogateId = r.ResourceSurrogateId,
	            previousVersion = r.Version,
		        previousIsDeleted = r.IsDeleted
        FROM   Resource r
	    WHERE  r.ResourceTypeId = restypeid
		        AND r.ResourceId = resid
		        AND r.IsHistory = 0 :: bit;

        IF previousResourceSurrogateId IS NULL THEN
		    version := 1;
	    ELSE
		    version := previousVersion + 1;

		    UPDATE resource
		    SET    IsHistory = 1 :: bit
		    WHERE  ResourceTypeId = restypeid
				    AND ResourceSurrogateId = previousResourceSurrogateId;
		    UPDATE TokenText
		    SET    IsHistory = 1 :: bit
		    WHERE  ResourceTypeId = restypeid
				    AND ResourceSurrogateId = previousResourceSurrogateId;
		
	    END IF;
        	    
        resSurrogateId := baseResourceSurrogateId + nextval('ResourceSurrogateIdUniquifierSequence');
	    IF version = 1 THEN
		    isRawResourceMetaSet := 1 :: bit;
	    ELSE
		    isRawResourceMetaSet := 0 :: bit;
	    END IF;

        INSERT INTO Resource (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash)
            VALUES(restypeid, resid, version, 0 :: bit, resSurrogateId, isDeleted, requestMethod, rawResource, isRawResourceMetaSet, searchParamHash);

        << second_loop>>
        FOREACH y in ARRAY tokentextsearchparams
	    LOOP
		    INSERT INTO TokenText (ResourceTypeId, ResourceSurrogateId, ResourceId, Version, SearchParamId, Text, IsHistory)
		    SELECT DISTINCT restypeid,
						    resSurrogateId,
                            resid,
                            version,
						    y.SearchParamId,
						    y.Text,
						    0 :: bit;
	    END LOOP second_loop;
	END IF;
END;
$BODY$;
