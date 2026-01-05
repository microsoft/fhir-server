CREATE PROCEDURE dbo.GetResourcesByTokens @ResourceTypeId smallint, @SearchParamId smallint, @Tokens dbo.TokenList READONLY, @Top int
AS
set nocount on
DECLARE @st datetime = getUTCdate()
       ,@SP varchar(100) = 'GetResourcesByTokens'
       ,@Mode varchar(100) = 'RT='+convert(varchar,@ResourceTypeId)+' SP='+convert(varchar,@SearchParamId)+' Tokens='+convert(varchar,(SELECT count(*) FROM @Tokens))+' T='+convert(varchar,@Top)
       ,@DummyTop bigint = 9223372036854775807

BEGIN TRY
  IF NOT EXISTS (SELECT * FROM @Tokens WHERE CodeOverflow IS NOT NULL OR SystemValue IS NOT NULL) -- fastest
    SELECT ResourceTypeId, ResourceId, Version, IsDeleted, ResourceSurrogateId, RequestMethod, IsMatch = convert(bit,1), IsPartial = convert(bit,0), IsRawResourceMetaSet, SearchParamHash, RawResource
      FROM (SELECT DISTINCT TOP (@Top) 
                   Sid1 = ResourceSurrogateId
              FROM (SELECT TOP (@DummyTop) * FROM @Tokens) A 
                   JOIN dbo.TokenSearchParam B
                     ON B.Code = A.Code 
                        AND (B.SystemId = A.SystemId OR A.SystemId IS NULL) -- Covered by include in secondary index
              WHERE ResourceTypeId = @ResourceTypeId	
                AND SearchParamId = @SearchParamId
              ORDER BY
                   ResourceSurrogateId
           ) A
           JOIN dbo.Resource ON ResourceSurrogateId = Sid1
      WHERE ResourceTypeId = @ResourceTypeId
      ORDER BY 
           ResourceSurrogateId
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
  ELSE IF NOT EXISTS (SELECT * FROM @Tokens WHERE CodeOverflow IS NOT NULL) -- sytem lookup but no overflow
    SELECT ResourceTypeId, ResourceId, Version, IsDeleted, ResourceSurrogateId, RequestMethod, IsMatch = convert(bit,1), IsPartial = convert(bit,0), IsRawResourceMetaSet, SearchParamHash, RawResource
      FROM (SELECT DISTINCT TOP (@Top) 
                   Sid1 = ResourceSurrogateId
              FROM (SELECT TOP (@DummyTop)
                           Code
                          ,CodeOverflow
                          ,SystemId = CASE WHEN SystemValue IS NOT NULL THEN (SELECT SystemId FROM dbo.System WHERE Value = SystemValue) ELSE SystemId END
                          ,SystemValue
                      FROM @Tokens
                   ) A
                   JOIN dbo.TokenSearchParam B
                     ON B.Code = A.Code 
                        AND (B.SystemId = A.SystemId OR A.SystemId IS NULL AND A.SystemValue IS NULL) -- Covered by include in secondary index
              WHERE ResourceTypeId = @ResourceTypeId	
                AND SearchParamId = @SearchParamId
              ORDER BY
                   ResourceSurrogateId
           ) A
           JOIN dbo.Resource ON ResourceSurrogateId = Sid1
      WHERE ResourceTypeId = @ResourceTypeId
      ORDER BY 
           ResourceSurrogateId
      OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
  ELSE
    SELECT ResourceTypeId, ResourceId, Version, IsDeleted, ResourceSurrogateId, RequestMethod, IsMatch = convert(bit,1), IsPartial = convert(bit,0), IsRawResourceMetaSet, SearchParamHash, RawResource
      FROM (SELECT DISTINCT TOP (@Top) 
                   Sid1 = ResourceSurrogateId
              FROM (SELECT TOP (@DummyTop)
                           Code
                          ,CodeOverflow
                          ,SystemId = CASE WHEN SystemValue IS NOT NULL THEN (SELECT SystemId FROM dbo.System WHERE Value = SystemValue) ELSE SystemId END
                          ,SystemValue
                      FROM @Tokens
                   ) A
                   JOIN dbo.TokenSearchParam B
                     ON B.Code = A.Code 
                        AND (B.CodeOverflow = A.CodeOverflow OR B.CodeOverflow IS NULL AND A.CodeOverflow IS NULL) -- Causes key lookup
                        AND (B.SystemId = A.SystemId OR A.SystemId IS NULL AND A.SystemValue IS NULL) -- Covered by include in secondary index
              WHERE ResourceTypeId = @ResourceTypeId	
                AND SearchParamId = @SearchParamId
              ORDER BY
                   ResourceSurrogateId
           ) A
           JOIN dbo.Resource ON ResourceSurrogateId = Sid1
      WHERE ResourceTypeId = @ResourceTypeId
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
