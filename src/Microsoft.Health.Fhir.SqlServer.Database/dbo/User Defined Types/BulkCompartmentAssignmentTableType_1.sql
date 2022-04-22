CREATE TYPE [dbo].[BulkCompartmentAssignmentTableType_1] AS TABLE (
    [Offset]              INT          NOT NULL,
    [CompartmentTypeId]   TINYINT      NOT NULL,
    [ReferenceResourceId] VARCHAR (64) COLLATE Latin1_General_100_CS_AS NOT NULL);

