CREATE TABLE [dbo].[DateTimeSearchParam] (
    [ResourceTypeId]      SMALLINT      NOT NULL,
    [ResourceSurrogateId] BIGINT        NOT NULL,
    [SearchParamId]       SMALLINT      NOT NULL,
    [StartDateTime]       DATETIME2 (7) NOT NULL,
    [EndDateTime]         DATETIME2 (7) NOT NULL,
    [IsLongerThanADay]    BIT           NOT NULL,
    [IsHistory]           BIT           NOT NULL,
    [IsMin]               BIT           CONSTRAINT [date_IsMin_Constraint] DEFAULT ((0)) NOT NULL,
    [IsMax]               BIT           CONSTRAINT [date_IsMax_Constraint] DEFAULT ((0)) NOT NULL
) ON [PartitionScheme_ResourceTypeId] ([ResourceTypeId]);


GO
ALTER TABLE [dbo].[DateTimeSearchParam] SET (LOCK_ESCALATION = AUTO);


GO
CREATE CLUSTERED INDEX [IXC_DateTimeSearchParam]
    ON [dbo].[DateTimeSearchParam]([ResourceTypeId] ASC, [ResourceSurrogateId] ASC, [SearchParamId] ASC)
    ON [PartitionScheme_ResourceTypeId] ([ResourceTypeId]);


GO
CREATE NONCLUSTERED INDEX [IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime]
    ON [dbo].[DateTimeSearchParam]([ResourceTypeId] ASC, [SearchParamId] ASC, [StartDateTime] ASC, [EndDateTime] ASC, [ResourceSurrogateId] ASC)
    INCLUDE([IsLongerThanADay], [IsMin], [IsMax]) WHERE ([IsHistory]=(0))
    ON [PartitionScheme_ResourceTypeId] ([ResourceTypeId]);


GO
CREATE NONCLUSTERED INDEX [IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime]
    ON [dbo].[DateTimeSearchParam]([ResourceTypeId] ASC, [SearchParamId] ASC, [EndDateTime] ASC, [StartDateTime] ASC, [ResourceSurrogateId] ASC)
    INCLUDE([IsLongerThanADay], [IsMin], [IsMax]) WHERE ([IsHistory]=(0))
    ON [PartitionScheme_ResourceTypeId] ([ResourceTypeId]);


GO
CREATE NONCLUSTERED INDEX [IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime_Long]
    ON [dbo].[DateTimeSearchParam]([ResourceTypeId] ASC, [SearchParamId] ASC, [StartDateTime] ASC, [EndDateTime] ASC, [ResourceSurrogateId] ASC)
    INCLUDE([IsMin], [IsMax]) WHERE ([IsHistory]=(0) AND [IsLongerThanADay]=(1))
    ON [PartitionScheme_ResourceTypeId] ([ResourceTypeId]);


GO
CREATE NONCLUSTERED INDEX [IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime_Long]
    ON [dbo].[DateTimeSearchParam]([ResourceTypeId] ASC, [SearchParamId] ASC, [EndDateTime] ASC, [StartDateTime] ASC, [ResourceSurrogateId] ASC)
    INCLUDE([IsMin], [IsMax]) WHERE ([IsHistory]=(0) AND [IsLongerThanADay]=(1))
    ON [PartitionScheme_ResourceTypeId] ([ResourceTypeId]);

