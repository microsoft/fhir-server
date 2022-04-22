CREATE TABLE [dbo].[NumberSearchParam] (
    [ResourceTypeId]      SMALLINT        NOT NULL,
    [ResourceSurrogateId] BIGINT          NOT NULL,
    [SearchParamId]       SMALLINT        NOT NULL,
    [SingleValue]         DECIMAL (18, 6) NULL,
    [LowValue]            DECIMAL (18, 6) NOT NULL,
    [HighValue]           DECIMAL (18, 6) NOT NULL,
    [IsHistory]           BIT             NOT NULL
) ON [PartitionScheme_ResourceTypeId] ([ResourceTypeId]);


GO
ALTER TABLE [dbo].[NumberSearchParam] SET (LOCK_ESCALATION = AUTO);


GO
CREATE CLUSTERED INDEX [IXC_NumberSearchParam]
    ON [dbo].[NumberSearchParam]([ResourceTypeId] ASC, [ResourceSurrogateId] ASC, [SearchParamId] ASC)
    ON [PartitionScheme_ResourceTypeId] ([ResourceTypeId]);


GO
CREATE NONCLUSTERED INDEX [IX_NumberSearchParam_SearchParamId_SingleValue]
    ON [dbo].[NumberSearchParam]([ResourceTypeId] ASC, [SearchParamId] ASC, [SingleValue] ASC, [ResourceSurrogateId] ASC) WHERE ([IsHistory]=(0) AND [SingleValue] IS NOT NULL)
    ON [PartitionScheme_ResourceTypeId] ([ResourceTypeId]);


GO
CREATE NONCLUSTERED INDEX [IX_NumberSearchParam_SearchParamId_LowValue_HighValue]
    ON [dbo].[NumberSearchParam]([ResourceTypeId] ASC, [SearchParamId] ASC, [LowValue] ASC, [HighValue] ASC, [ResourceSurrogateId] ASC) WHERE ([IsHistory]=(0))
    ON [PartitionScheme_ResourceTypeId] ([ResourceTypeId]);


GO
CREATE NONCLUSTERED INDEX [IX_NumberSearchParam_SearchParamId_HighValue_LowValue]
    ON [dbo].[NumberSearchParam]([ResourceTypeId] ASC, [SearchParamId] ASC, [HighValue] ASC, [LowValue] ASC, [ResourceSurrogateId] ASC) WHERE ([IsHistory]=(0))
    ON [PartitionScheme_ResourceTypeId] ([ResourceTypeId]);

