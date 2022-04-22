CREATE TABLE [dbo].[ExportJob] (
    [Id]                VARCHAR (64)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    [Hash]              VARCHAR (64)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    [Status]            VARCHAR (10)  NOT NULL,
    [HeartbeatDateTime] DATETIME2 (7) NULL,
    [RawJobRecord]      VARCHAR (MAX) NOT NULL,
    [JobVersion]        ROWVERSION    NOT NULL,
    CONSTRAINT [PKC_ExportJob] PRIMARY KEY CLUSTERED ([Id] ASC)
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [IX_ExportJob_Hash_Status_HeartbeatDateTime]
    ON [dbo].[ExportJob]([Hash] ASC, [Status] ASC, [HeartbeatDateTime] ASC);

