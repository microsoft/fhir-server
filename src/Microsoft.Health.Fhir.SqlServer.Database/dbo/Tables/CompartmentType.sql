CREATE TABLE [dbo].[CompartmentType] (
    [CompartmentTypeId] TINYINT       IDENTITY (1, 1) NOT NULL,
    [Name]              VARCHAR (128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CONSTRAINT [PKC_CompartmentType] PRIMARY KEY CLUSTERED ([Name] ASC) WITH (DATA_COMPRESSION = PAGE),
    CONSTRAINT [UQ_CompartmentType_CompartmentTypeId] UNIQUE NONCLUSTERED ([CompartmentTypeId] ASC)
);

