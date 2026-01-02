CREATE TYPE dbo.TokenList AS TABLE
(
    Code         varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,CodeOverflow varchar(max) COLLATE Latin1_General_100_CS_AS NULL
   ,SystemId     int          NULL
)
GO
