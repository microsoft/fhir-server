
CREATE TABLE dbo.EmbeddingModel
(
    EmbeddingModelId        smallint        IDENTITY(1,1)   NOT NULL,
    ModelName               varchar(128)    COLLATE Latin1_General_100_CS_AS NOT NULL,
    ModelVersion            varchar(64)     COLLATE Latin1_General_100_CS_AS NOT NULL,
    Dimension               int             NOT NULL,
    DistanceMetric          varchar(16)     COLLATE Latin1_General_100_CS_AS NOT NULL
        CONSTRAINT DF_EmbeddingModel_DistanceMetric DEFAULT 'cosine',
    CreatedAt               datetime2(7)    NOT NULL
        CONSTRAINT DF_EmbeddingModel_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PKC_EmbeddingModel PRIMARY KEY CLUSTERED (EmbeddingModelId),
    CONSTRAINT U_EmbeddingModel_Name_Version UNIQUE (ModelName, ModelVersion)
)
