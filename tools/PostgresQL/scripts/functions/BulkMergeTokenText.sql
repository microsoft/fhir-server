-- FUNCTION: public.bulkmergetokentext(bulkimportresourcetype_1, bulktokentexttabletype_2[])

-- DROP FUNCTION IF EXISTS public.bulkmergetokentext(bulkimportresourcetype_1, bulktokentexttabletype_2[]);

CREATE OR REPLACE FUNCTION public.bulkmergetokentext(
	resource bulkimportresourcetype_1,
	tokentexts bulktokentexttabletype_2[])
    RETURNS text
    LANGUAGE 'plpgsql'
    COST 100
    VOLATILE PARALLEL UNSAFE
AS $BODY$
DECLARE
	val bulktokentexttabletype_2;
BEGIN
	<<first_loop>>
	FOREACH val IN ARRAY tokentexts
	LOOP
		
		INSERT INTO tokentext(
		resourcetypeid, resourcesurrogateid, resourceid, version, searchparamid, text, ishistory)
		SELECT resource.resourcetypeid, resource.resourcesurrogateid, resource.resourceid, resource.version, val.searchparamid, val.text, resource.ishistory 
		ON CONFLICT (resourcetypeid, resourceid, version) DO NOTHING;
	END LOOP first_loop;
	RETURN 'end';
END;
$BODY$;