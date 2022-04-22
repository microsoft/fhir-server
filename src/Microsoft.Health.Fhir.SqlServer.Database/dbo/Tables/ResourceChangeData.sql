CREATE TABLE [dbo].[ResourceChangeData] (
    [Id]                   BIGINT        IDENTITY (1, 1) NOT NULL,
    [Timestamp]            DATETIME2 (7) CONSTRAINT [DF_ResourceChangeData_Timestamp] DEFAULT (sysutcdatetime()) NOT NULL,
    [ResourceId]           VARCHAR (64)  NOT NULL,
    [ResourceTypeId]       SMALLINT      NOT NULL,
    [ResourceVersion]      INT           NOT NULL,
    [ResourceChangeTypeId] TINYINT       NOT NULL
) ON [PartitionScheme_ResourceChangeData_Timestamp] ([Timestamp]);


GO
CREATE CLUSTERED INDEX [IXC_ResourceChangeData]
    ON [dbo].[ResourceChangeData]([Id] ASC)
    ON [PartitionScheme_ResourceChangeData_Timestamp] ([Timestamp]);

