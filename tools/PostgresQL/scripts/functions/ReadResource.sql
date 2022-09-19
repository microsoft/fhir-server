-- FUNCTION: public.readresource(smallint, character varying, integer)

-- DROP FUNCTION IF EXISTS public.readresource(smallint, character varying, integer);

CREATE OR REPLACE FUNCTION public.readresource(
	restypeid smallint,
	resid character varying,
	vers integer)
    RETURNS TABLE(resourcesurrogateid bigint, version integer, isdeleted bit, ishistory bit, rawresource bytea, israwresourcemetaset bit, searchparamhash character varying) 
    LANGUAGE 'plpgsql'
    COST 100
    VOLATILE PARALLEL UNSAFE
    ROWS 1000

AS $BODY$

BEGIN
    RETURN QUERY
        SELECT r.resourcesurrogateid, r.version, r.isdeleted, r.ishistory, r.rawresource, r.israwresourcemetaset, r.searchparamhash
        FROM resource r
        WHERE r.resourcetypeid=restypeid AND r.resourceid=resid AND r.version=vers;
END;
$BODY$;

ALTER FUNCTION public.readresource(smallint, character varying, integer)
    OWNER TO postgres;
