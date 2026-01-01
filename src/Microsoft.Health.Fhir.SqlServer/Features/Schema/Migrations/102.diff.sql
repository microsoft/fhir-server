IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'TokenList')
CREATE TYPE dbo.TokenList AS TABLE
(
    Code         varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,CodeOverflow varchar(max) COLLATE Latin1_General_100_CS_AS NULL
   ,SystemId     int          NULL
)
GO
CREATE OR ALTER PROCEDURE dbo.GetResourcesByTokens @ResourceTypeId smallint, @SearchParamId smallint, @Tokens dbo.TokenList READONLY, @Top int
AS
set nocount on
DECLARE @st datetime = getUTCdate()
       ,@SP varchar(100) = 'GetResourcesByTokens'
       ,@Mode varchar(100) = 'Tokens='+convert(varchar,(SELECT count(*) FROM @Tokens))
       ,@DummyTop bigint = 9223372036854775807

BEGIN TRY
  IF EXISTS (SELECT * FROM @Tokens WHERE CodeOverflow IS NOT NULL)
    WITH cte0 AS
      (SELECT T1 = ResourceTypeId
             ,Sid1 = ResourceSurrogateId
         FROM dbo.TokenSearchParam A
              JOIN (SELECT TOP (@DummyTop) * FROM @Tokens) B 
                ON B.Code = A.Code 
                   AND (B.CodeOverflow = A.CodeOverflow OR B.CodeOverflow IS NULL AND A.CodeOverflow IS NULL) -- Causes key lookup
                   AND (B.SystemId = A.SystemId OR B.SystemId IS NULL) -- Covered by include in secondary index
         WHERE ResourceTypeId = @ResourceTypeId	
           AND SearchParamId = @SearchParamId
      )
     ,cte1 AS
      (SELECT DISTINCT TOP (@Top) T1, Sid1, IsMatch = convert(bit,1), IsPartial = convert(bit,0) 
         FROM cte0
         ORDER BY T1, Sid1
      )
    SELECT ResourceTypeId, ResourceId, Version, IsDeleted, ResourceSurrogateId, RequestMethod, IsMatch, IsPartial, IsRawResourceMetaSet, SearchParamHash, RawResource
      FROM dbo.Resource
           JOIN cte1 ON ResourceTypeId = cte1.T1 AND ResourceSurrogateId = cte1.Sid1
      WHERE IsHistory = 0 
        AND IsDeleted = 0 
      ORDER BY 
           ResourceSurrogateId
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
  ELSE
    WITH cte0 AS
      (SELECT T1 = ResourceTypeId
             ,Sid1 = ResourceSurrogateId
         FROM dbo.TokenSearchParam A
              JOIN (SELECT TOP (@DummyTop) * FROM @Tokens) B 
                ON B.Code = A.Code 
                   AND (B.SystemId = A.SystemId OR B.SystemId IS NULL) -- Covered by include in secondary index
         WHERE ResourceTypeId = @ResourceTypeId	
           AND SearchParamId = @SearchParamId
      )
     ,cte1 AS
      (SELECT DISTINCT TOP (@Top) T1, Sid1, IsMatch = convert(bit,1), IsPartial = convert(bit,0) 
         FROM cte0
         ORDER BY T1, Sid1
      )
    SELECT ResourceTypeId, ResourceId, Version, IsDeleted, ResourceSurrogateId, RequestMethod, IsMatch, IsPartial, IsRawResourceMetaSet, SearchParamHash, RawResource
      FROM dbo.Resource
           JOIN cte1 ON ResourceTypeId = cte1.T1 AND ResourceSurrogateId = cte1.Sid1
      WHERE IsHistory = 0 
        AND IsDeleted = 0 
      ORDER BY 
           ResourceSurrogateId
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
