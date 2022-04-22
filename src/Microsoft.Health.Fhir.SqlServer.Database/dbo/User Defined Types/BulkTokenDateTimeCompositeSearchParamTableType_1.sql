CREATE TYPE [dbo].[BulkTokenDateTimeCompositeSearchParamTableType_1] AS TABLE (
    [Offset]            INT                NOT NULL,
    [SearchParamId]     SMALLINT           NOT NULL,
    [SystemId1]         INT                NULL,
    [Code1]             VARCHAR (128)      COLLATE Latin1_General_100_CS_AS NOT NULL,
    [StartDateTime2]    DATETIMEOFFSET (7) NOT NULL,
    [EndDateTime2]      DATETIMEOFFSET (7) NOT NULL,
    [IsLongerThanADay2] BIT                NOT NULL);

