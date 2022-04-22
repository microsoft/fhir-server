CREATE TABLE [dbo].[SchemaVersion] (
    [Version] INT          NOT NULL,
    [Status]  VARCHAR (10) NULL,
    PRIMARY KEY CLUSTERED ([Version] ASC)
);

