CREATE TABLE [dbo].[ResourceChangeDataStaging] (
    [Id]                   BIGINT        IDENTITY (1, 1) NOT NULL,
    [Timestamp]            DATETIME2 (7) CONSTRAINT [DF_ResourceChangeDataStaging_Timestamp] DEFAULT (sysutcdatetime()) NOT NULL,
    [ResourceId]           VARCHAR (64)  NOT NULL,
    [ResourceTypeId]       SMALLINT      NOT NULL,
    [ResourceVersion]      INT           NOT NULL,
    [ResourceChangeTypeId] TINYINT       NOT NULL,
    CONSTRAINT [CHK_ResourceChangeDataStaging_partition] CHECK ([Timestamp]<CONVERT([datetime2](7),N'9999-12-31 23:59:59.9999999'))
);


GO
CREATE CLUSTERED INDEX [IXC_ResourceChangeDataStaging]
    ON [dbo].[ResourceChangeDataStaging]([Id] ASC, [Timestamp] ASC);

