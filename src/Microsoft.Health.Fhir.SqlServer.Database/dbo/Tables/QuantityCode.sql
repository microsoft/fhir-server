CREATE TABLE [dbo].[QuantityCode] (
    [QuantityCodeId] INT            IDENTITY (1, 1) NOT NULL,
    [Value]          NVARCHAR (256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CONSTRAINT [PKC_QuantityCode] PRIMARY KEY CLUSTERED ([Value] ASC) WITH (DATA_COMPRESSION = PAGE),
    CONSTRAINT [UQ_QuantityCode_QuantityCodeId] UNIQUE NONCLUSTERED ([QuantityCodeId] ASC)
);

