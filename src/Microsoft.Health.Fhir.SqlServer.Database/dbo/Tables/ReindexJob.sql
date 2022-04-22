CREATE TABLE [dbo].[ReindexJob] (
    [Id]                VARCHAR (64)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    [Status]            VARCHAR (10)  NOT NULL,
    [HeartbeatDateTime] DATETIME2 (7) NULL,
    [RawJobRecord]      VARCHAR (MAX) NOT NULL,
    [JobVersion]        ROWVERSION    NOT NULL,
    CONSTRAINT [PKC_ReindexJob] PRIMARY KEY CLUSTERED ([Id] ASC)
);

