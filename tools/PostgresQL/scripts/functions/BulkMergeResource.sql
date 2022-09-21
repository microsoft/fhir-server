CREATE OR REPLACE FUNCTION public.bulkmergeresource(
	IN resources bulkimportresourcetype_1[]) 
	RETURNS SETOF BIGINT
    LANGUAGE 'plpgsql'
    
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
		ON CONFLICT (resourcetypeid, resourceid, version) DO NOTHING
		RETURNING resourcesurrogateid;
	END LOOP first_loop;
END;
$BODY$;