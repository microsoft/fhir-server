﻿--DROP PROCEDURE dbo.GetResources
GO
CREATE PROCEDURE dbo.GetResources @ResourceKeys dbo.ResourceKeyList READONLY
AS
set nocount on
DECLARE @st datetime = getUTCdate()
       ,@SP varchar(100) = 'GetResources'
       ,@InputRows int
       ,@NotNullVersionExists bit 
       ,@NullVersionExists bit
       ,@MinRT smallint
       ,@MaxRT smallint

SELECT @MinRT = min(ResourceTypeId), @MaxRT = max(ResourceTypeId), @InputRows = count(*), @NotNullVersionExists = max(CASE WHEN Version IS NOT NULL THEN 1 ELSE 0 END), @NullVersionExists = max(CASE WHEN Version IS NULL THEN 1 ELSE 0 END) FROM @ResourceKeys

DECLARE @Mode varchar(100) = 'RT=['+convert(varchar,@MinRT)+','+convert(varchar,@MaxRT)+'] Cnt='+convert(varchar,@InputRows)+' NNVE='+convert(varchar,@NotNullVersionExists)+' NVE='+convert(varchar,@NullVersionExists)

BEGIN TRY
  IF @NotNullVersionExists = 1
    IF @NullVersionExists = 0
      SELECT B.ResourceTypeId
            ,B.ResourceId
            ,ResourceSurrogateId
            ,C.Version
            ,IsDeleted
            ,IsHistory
            ,RawResource
            ,IsRawResourceMetaSet
            ,SearchParamHash
            ,FileId
            ,OffsetInFile
            ,ResourceLength
        FROM (SELECT * FROM @ResourceKeys) A
             INNER LOOP JOIN dbo.ResourceIdIntMap B WITH (INDEX = U_ResourceIdIntMap_ResourceId_ResourceTypeId) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
             INNER LOOP JOIN dbo.Resource C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceIdInt = B.ResourceIdInt AND C.Version = A.Version
        OPTION (MAXDOP 1)
    ELSE
      SELECT *
        FROM (SELECT B.ResourceTypeId
                    ,B.ResourceId
                    ,ResourceSurrogateId
                    ,C.Version
                    ,IsDeleted
                    ,IsHistory
                    ,RawResource
                    ,IsRawResourceMetaSet
                    ,SearchParamHash
                    ,FileId
                    ,OffsetInFile
                    ,ResourceLength
                FROM (SELECT * FROM @ResourceKeys WHERE Version IS NOT NULL) A
                     INNER LOOP JOIN dbo.ResourceIdIntMap B WITH (INDEX = U_ResourceIdIntMap_ResourceId_ResourceTypeId) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
                     INNER LOOP JOIN dbo.Resource C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceIdInt = B.ResourceIdInt AND C.Version = A.Version
              UNION ALL
              SELECT B.ResourceTypeId
                    ,B.ResourceId
                    ,C.ResourceSurrogateId
                    ,C.Version
                    ,IsDeleted
                    ,IsHistory
                    ,RawResource
                    ,IsRawResourceMetaSet
                    ,SearchParamHash
                    ,FileId
                    ,OffsetInFile
                    ,ResourceLength
                FROM (SELECT * FROM @ResourceKeys WHERE Version IS NULL) A
                     INNER LOOP JOIN dbo.ResourceIdIntMap B WITH (INDEX = U_ResourceIdIntMap_ResourceId_ResourceTypeId) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
                     INNER LOOP JOIN dbo.CurrentResources C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceIdInt = B.ResourceIdInt AND C.IsHistory = 0
                     LEFT OUTER JOIN dbo.RawResources D ON D.ResourceTypeId = A.ResourceTypeId AND D.ResourceSurrogateId = C.ResourceSurrogateId
             ) A
        OPTION (MAXDOP 1)
  ELSE
    SELECT B.ResourceTypeId
          ,B.ResourceId
          ,C.ResourceSurrogateId
          ,C.Version
          ,IsDeleted
          ,IsHistory
          ,RawResource
          ,IsRawResourceMetaSet
          ,SearchParamHash
          ,FileId
          ,OffsetInFile
          ,ResourceLength
      FROM (SELECT * FROM @ResourceKeys) A
           INNER LOOP JOIN dbo.ResourceIdIntMap B WITH (INDEX = U_ResourceIdIntMap_ResourceId_ResourceTypeId) ON B.ResourceTypeId = A.ResourceTypeId AND B.ResourceId = A.ResourceId
           INNER LOOP JOIN dbo.CurrentResources C ON C.ResourceTypeId = A.ResourceTypeId AND C.ResourceIdInt = B.ResourceIdInt
           LEFT OUTER JOIN dbo.RawResources D ON D.ResourceTypeId = A.ResourceTypeId AND D.ResourceSurrogateId = C.ResourceSurrogateId
      OPTION (MAXDOP 1)

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
--DECLARE @ResourceKeys dbo.ResourceKeyList
--INSERT INTO @ResourceKeys SELECT 96, newid(), NULL
--EXECUTE dbo.GetResources @ResourceKeys
