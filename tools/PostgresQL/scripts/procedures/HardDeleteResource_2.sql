/*************************************************************
    Stored procedures - HardDeleteResource_2
**************************************************************/
--
-- STORED PROCEDURE
--     Deletes a single resource's history, and optionally the resource itself
--
-- DESCRIPTION
--     Permanently deletes all history data related to a resource. Optionally removes all data, including the current resource version.
--     Data remains recoverable from the transaction log, however.
--
-- PARAMETERS
--     @resourceTypeId
--         * The ID of the resource type (See ResourceType table)
--     @resourceId
--         * The resource ID (must be the same as in the resource itself)
--     @keepCurrentVersion
--         * When 1, the current resource version kept, else all data is removed.
--
CREATE OR REPLACE PROCEDURE public.harddeleteResource(
	IN restypeid smallint,
	IN resid character varying)
language plpgsql    
as $$
begin
	DELETE FROM Resource
	WHERE  ResourceTypeId = restypeid
			AND ResourceId = resid;

	DELETE FROM TokenText
	WHERE  ResourceTypeId = restypeid
			AND ResourceId = resid;

    commit;
end;$$;
