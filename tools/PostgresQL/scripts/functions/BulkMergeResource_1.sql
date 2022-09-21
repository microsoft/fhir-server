-- FUNCTION: public.bulkmergeresource_1(bulkimportresourcetype_1[])

-- DROP FUNCTION IF EXISTS public.bulkmergeresource_1(bulkimportresourcetype_1[]);

CREATE OR REPLACE FUNCTION public.bulkmergeresource_1(
	resources bulkimportresourcetype_1[])
    RETURNS text
    LANGUAGE 'plpgsql'
    COST 100
    VOLATILE PARALLEL UNSAFE
AS $BODY$
DECLARE
	res bulkimportresourcetype_1;

BEGIN

	<<first_loop>>
	FOREACH res IN ARRAY resources
	LOOP
		
		INSERT INTO resource(
		resourcetypeid, resourceid, version, ishistory, resourcesurrogateid, isdeleted, requestmethod, rawresource, israwresourcemetaset, searchparamhash)
		SELECT res.resourcetypeid,
			   res.resourceid,
			   res.version,
			   res.ishistory,
			   res.resourcesurrogateid,
			   res.isdeleted, 
			   res.requestmethod, 
			   res.rawresource, 
			   res.israwresourcemetaset, 
			   res.searchparamhash
		ON CONFLICT (resourcetypeid, resourceid, version) DO NOTHING;
		
	END LOOP first_loop;
	RETURN 'end';
END;
$BODY$;
