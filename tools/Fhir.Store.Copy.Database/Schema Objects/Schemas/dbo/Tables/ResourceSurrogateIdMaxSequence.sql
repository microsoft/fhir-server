--DROP TABLE dbo.ResourceSurrogateIdSequence
GO
CREATE TABLE dbo.ResourceSurrogateIdMaxSequence
(
    MaxSequence bigint NOT NULL
)
GO
CREATE SEQUENCE dbo.ResourceSurrogateIdSequence AS bigint START WITH 0 INCREMENT BY 1 CACHE 1000000
GO
CREATE SEQUENCE dbo.ResourceSurrogateIdUniquifierSequence
        AS int
        START WITH 0
        INCREMENT BY 1
        MINVALUE 0
        MAXVALUE 79999
        CYCLE
        CACHE 1000000
GO
