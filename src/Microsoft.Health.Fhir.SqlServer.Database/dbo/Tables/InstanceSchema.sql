CREATE TABLE [dbo].[InstanceSchema] (
    [Name]           VARCHAR (64)  COLLATE Latin1_General_100_CS_AS NOT NULL,
    [CurrentVersion] INT           NOT NULL,
    [MaxVersion]     INT           NOT NULL,
    [MinVersion]     INT           NOT NULL,
    [Timeout]        DATETIME2 (0) NOT NULL
);


GO
CREATE UNIQUE CLUSTERED INDEX [IXC_InstanceSchema]
    ON [dbo].[InstanceSchema]([Name] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_InstanceSchema_Timeout]
    ON [dbo].[InstanceSchema]([Timeout] ASC);

