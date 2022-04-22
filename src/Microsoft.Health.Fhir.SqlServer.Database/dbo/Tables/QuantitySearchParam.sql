CREATE TABLE [dbo].[QuantitySearchParam] (
    [ResourceTypeId]      SMALLINT        NOT NULL,
    [ResourceSurrogateId] BIGINT          NOT NULL,
    [SearchParamId]       SMALLINT        NOT NULL,
    [SystemId]            INT             NULL,
    [QuantityCodeId]      INT             NULL,
    [SingleValue]         DECIMAL (18, 6) NULL,
    [LowValue]            DECIMAL (18, 6) NOT NULL,
    [HighValue]           DECIMAL (18, 6) NOT NULL,
    [IsHistory]           BIT             NOT NULL
) ON [PartitionScheme_ResourceTypeId] ([ResourceTypeId]);


GO
ALTER TABLE [dbo].[QuantitySearchParam] SET (LOCK_ESCALATION = AUTO);


GO
CREATE CLUSTERED INDEX [IXC_QuantitySearchParam]
    ON [dbo].[QuantitySearchParam]([ResourceTypeId] ASC, [ResourceSurrogateId] ASC, [SearchParamId] ASC)
    ON [PartitionScheme_ResourceTypeId] ([ResourceTypeId]);


GO
CREATE NONCLUSTERED INDEX [IX_QuantitySearchParam_SearchParamId_QuantityCodeId_SingleValue]
    ON [dbo].[QuantitySearchParam]([ResourceTypeId] ASC, [SearchParamId] ASC, [QuantityCodeId] ASC, [SingleValue] ASC, [ResourceSurrogateId] ASC)
    INCLUDE([SystemId]) WHERE ([IsHistory]=(0) AND [SingleValue] IS NOT NULL)
    ON [PartitionScheme_ResourceTypeId] ([ResourceTypeId]);


GO
CREATE NONCLUSTERED INDEX [IX_QuantitySearchParam_SearchParamId_QuantityCodeId_LowValue_HighValue]
    ON [dbo].[QuantitySearchParam]([ResourceTypeId] ASC, [SearchParamId] ASC, [QuantityCodeId] ASC, [LowValue] ASC, [HighValue] ASC, [ResourceSurrogateId] ASC)
    INCLUDE([SystemId]) WHERE ([IsHistory]=(0))
    ON [PartitionScheme_ResourceTypeId] ([ResourceTypeId]);


GO
CREATE NONCLUSTERED INDEX [IX_QuantitySearchParam_SearchParamId_QuantityCodeId_HighValue_LowValue]
    ON [dbo].[QuantitySearchParam]([ResourceTypeId] ASC, [SearchParamId] ASC, [QuantityCodeId] ASC, [HighValue] ASC, [LowValue] ASC, [ResourceSurrogateId] ASC)
    INCLUDE([SystemId]) WHERE ([IsHistory]=(0))
    ON [PartitionScheme_ResourceTypeId] ([ResourceTypeId]);

