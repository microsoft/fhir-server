-- Type: bulkimportresourcetype_1

-- DROP TYPE IF EXISTS public.bulkimportresourcetype_1;

CREATE TYPE public.bulkimportresourcetype_1 AS
(
	resourcetypeid smallint,
	resourceid character varying(64),
	version integer,
	ishistory bit(1),
	resourcesurrogateid bigint,
	isdeleted bit(1),
	requestmethod character varying(10),
	rawresource bytea,
	israwresourcemetaset bit(1),
	searchparamhash character varying(64)
);