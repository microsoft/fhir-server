// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlCommandSimplifierTests
    {
        private ILogger _logger;

        private readonly string _iterativeIncludeStartingQuery = "DECLARE @p0 int = 11\r\nDECLARE @p1 int = 11\r\nDECLARE @p2 int = 100\r\nDECLARE @p3 int = 100\r\nDECLARE @p4 int = 100\r\nDECLARE @p5 int = 100\r\n\r\nSET STATISTICS IO ON;\r\nSET STATISTICS TIME ON;\r\n\r\nDECLARE @FilteredData AS TABLE (T1 smallint, Sid1 bigint, IsMatch bit, IsPartial bit, Row int)\r\n;WITH\r\ncte0 AS\r\n(\r\n  SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\r\n  FROM dbo.Resource\r\n  WHERE IsHistory = 0\r\n\t  AND IsDeleted = 0\r\n\t  AND ResourceTypeId = 40\r\n)\r\n,cte1 AS\r\n(\r\n  SELECT row_number() OVER (ORDER BY T1 ASC, Sid1 ASC) AS Row, *\r\n  FROM\r\n  (\r\n\t  SELECT DISTINCT TOP (@p0) T1, Sid1, 1 AS IsMatch, 0 AS IsPartial\r\n\t  FROM cte0\r\n\t  ORDER BY T1 ASC, Sid1 ASC\r\n  ) t\r\n)\r\nINSERT INTO @FilteredData SELECT T1, Sid1, IsMatch, IsPartial, Row FROM cte1\r\n;WITH cte1 AS (SELECT * FROM @FilteredData)\r\n,cte2 AS\r\n(\r\n  SELECT DISTINCT refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1, 0 AS IsMatch\r\n  FROM dbo.ReferenceSearchParam refSource\r\n\t   JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId\r\n  WHERE refSource.SearchParamId = 204\r\n\t  AND refTarget.IsHistory = 0\r\n\t  AND refTarget.IsDeleted = 0\r\n\t  AND refSource.ResourceTypeId IN (40)\r\n\t  AND EXISTS (SELECT * FROM cte1 WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1 AND Row < @p1)\r\n)\r\n,cte3 AS\r\n(\r\n  SELECT DISTINCT T1, Sid1, IsMatch, 0 AS IsPartial\r\n  FROM cte2\r\n)\r\n,cte4 AS\r\n(\r\n  SELECT DISTINCT refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1, 0 AS IsMatch\r\n  FROM dbo.ReferenceSearchParam refSource\r\n\t   JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId\r\n  WHERE refSource.SearchParamId = 404\r\n\t  AND refTarget.IsHistory = 0\r\n\t  AND refTarget.IsDeleted = 0\r\n\t  AND refSource.ResourceTypeId IN (40)\r\n\t  AND EXISTS (SELECT * FROM cte1 WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1)\r\n)\r\n,cte5 AS\r\n(\r\n  SELECT DISTINCT T1, Sid1, IsMatch, 0 AS IsPartial\r\n  FROM cte4\r\n)\r\n,cte6 AS\r\n(\r\n  SELECT DISTINCT refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1, 0 AS IsMatch\r\n  FROM dbo.ReferenceSearchParam refSource\r\n\t   JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId\r\n  WHERE refSource.SearchParamId = 204\r\n\t  AND refTarget.IsHistory = 0\r\n\t  AND refTarget.IsDeleted = 0\r\n\t  AND refSource.ResourceTypeId IN (124)\r\n\t  AND EXISTS (SELECT * FROM cte5 WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1)\r\n)\r\n,cte7 AS\r\n(\r\n  SELECT DISTINCT T1, Sid1, IsMatch, 0 AS IsPartial\r\n  FROM cte6\r\n)\r\n,cte8 AS\r\n(\r\n  SELECT DISTINCT refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1, 0 AS IsMatch\r\n  FROM dbo.ReferenceSearchParam refSource\r\n\t   JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId\r\n  WHERE refSource.SearchParamId = 470\r\n\t  AND refTarget.IsHistory = 0\r\n\t  AND refTarget.IsDeleted = 0\r\n\t  AND refSource.ResourceTypeId IN (44)\r\n\t  AND EXISTS (SELECT * FROM cte3 WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1)\r\n)\r\n,cte9 AS\r\n(\r\n  SELECT DISTINCT TOP (@p2) T1, Sid1, IsMatch, CASE WHEN count_big(*) over() > @p3 THEN 1 ELSE 0 END AS IsPartial\r\n  FROM cte8\r\n)\r\n,cte10 AS\r\n(\r\n  SELECT DISTINCT refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1, 0 AS IsMatch\r\n  FROM dbo.ReferenceSearchParam refSource\r\n\t   JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId\r\n  WHERE refSource.SearchParamId = 470\r\n\t  AND refTarget.IsHistory = 0\r\n\t  AND refTarget.IsDeleted = 0\r\n\t  AND refSource.ResourceTypeId IN (44)\r\n\t  AND EXISTS (SELECT * FROM cte7 WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1)\r\n)\r\n,cte11 AS\r\n(\r\n  SELECT DISTINCT T1, Sid1, IsMatch, 0 AS IsPartial\r\n  FROM cte10\r\n)\r\n,cte12 AS\r\n(\r\n  SELECT DISTINCT refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1, 0 AS IsMatch\r\n  FROM dbo.ReferenceSearchParam refSource\r\n\t   JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId\r\n  WHERE refSource.SearchParamId = 770\r\n\t  AND refTarget.IsHistory = 0\r\n\t  AND refTarget.IsDeleted = 0\r\n\t  AND refSource.ResourceTypeId IN (71)\r\n\t  AND EXISTS (SELECT * FROM cte9 WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1)\r\n)\r\n,cte13 AS\r\n(\r\n  SELECT DISTINCT TOP (@p4) T1, Sid1, IsMatch, CASE WHEN count_big(*) over() > @p5 THEN 1 ELSE 0 END AS IsPartial\r\n  FROM cte12\r\n)\r\n,cte14 AS\r\n(\r\n  SELECT DISTINCT refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1, 0 AS IsMatch\r\n  FROM dbo.ReferenceSearchParam refSource\r\n\t   JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId\r\n  WHERE refSource.SearchParamId = 770\r\n\t  AND refTarget.IsHistory = 0\r\n\t  AND refTarget.IsDeleted = 0\r\n\t  AND refSource.ResourceTypeId IN (71)\r\n\t  AND EXISTS (SELECT * FROM cte11 WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1)\r\n)\r\n,cte15 AS\r\n(\r\n  SELECT DISTINCT T1, Sid1, IsMatch, 0 AS IsPartial\r\n  FROM cte14\r\n)\r\n,cte16 AS\r\n(\r\n  SELECT T1, Sid1, IsMatch, IsPartial\r\n  FROM cte1\r\n  UNION ALL\r\n  SELECT T1, Sid1, IsMatch, IsPartial\r\n  FROM cte3 WHERE NOT EXISTS (SELECT * FROM cte1 WHERE cte1.Sid1 = cte3.Sid1 AND cte1.T1 = cte3.T1)\r\n  UNION ALL\r\n  SELECT T1, Sid1, IsMatch, IsPartial\r\n  FROM cte5 WHERE NOT EXISTS (SELECT * FROM cte1 WHERE cte1.Sid1 = cte5.Sid1 AND cte1.T1 = cte5.T1)\r\n  UNION ALL\r\n  SELECT T1, Sid1, IsMatch, IsPartial\r\n  FROM cte7 WHERE NOT EXISTS (SELECT * FROM cte1 WHERE cte1.Sid1 = cte7.Sid1 AND cte1.T1 = cte7.T1)\r\n  UNION ALL\r\n  SELECT T1, Sid1, IsMatch, IsPartial\r\n  FROM cte9 WHERE NOT EXISTS (SELECT * FROM cte1 WHERE cte1.Sid1 = cte9.Sid1 AND cte1.T1 = cte9.T1)\r\n  UNION ALL\r\n  SELECT T1, Sid1, IsMatch, IsPartial\r\n  FROM cte11 WHERE NOT EXISTS (SELECT * FROM cte1 WHERE cte1.Sid1 = cte11.Sid1 AND cte1.T1 = cte11.T1)\r\n  UNION ALL\r\n  SELECT T1, Sid1, IsMatch, IsPartial\r\n  FROM cte13 WHERE NOT EXISTS (SELECT * FROM cte1 WHERE cte1.Sid1 = cte13.Sid1 AND cte1.T1 = cte13.T1)\r\n  UNION ALL\r\n  SELECT T1, Sid1, IsMatch, IsPartial\r\n  FROM cte15 WHERE NOT EXISTS (SELECT * FROM cte1 WHERE cte1.Sid1 = cte15.Sid1 AND cte1.T1 = cte15.T1)\r\n)\r\nSELECT DISTINCT r.ResourceTypeId, r.ResourceId, r.Version, r.IsDeleted, r.ResourceSurrogateId, r.RequestMethod, CAST(IsMatch AS bit) AS IsMatch, CAST(IsPartial AS bit) AS IsPartial, r.IsRawResourceMetaSet, r.SearchParamHash, r.RawResource\r\nFROM dbo.Resource r\r\n   JOIN cte16 ON r.ResourceTypeId = cte16.T1 AND r.ResourceSurrogateId = cte16.Sid1\r\nWHERE IsHistory = 0\r\n  AND IsDeleted = 0\r\nORDER BY IsMatch DESC, r.ResourceTypeId ASC, r.ResourceSurrogateId ASC\r\n\r\nOPTION (RECOMPILE)\r\n-- execution timeout = 180 sec.";

        private readonly string _iterativeIncludeEndingQuery = "DECLARE @p0 int = 11\r\nDECLARE @p1 int = 11\r\nDECLARE @p2 int = 100\r\nDECLARE @p3 int = 100\r\nDECLARE @p4 int = 100\r\nDECLARE @p5 int = 100\r\n\r\nSET STATISTICS IO ON;\r\nSET STATISTICS TIME ON;\r\n\r\nDECLARE @FilteredData AS TABLE (T1 smallint, Sid1 bigint, IsMatch bit, IsPartial bit, Row int)\r\n;WITH\r\ncte0 AS\r\n(\r\n  SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\r\n  FROM dbo.Resource\r\n  WHERE IsHistory = 0\r\n\t  AND IsDeleted = 0\r\n\t  AND ResourceTypeId = 40\r\n)\r\n,cte1 AS\r\n(\r\n  SELECT row_number() OVER (ORDER BY T1 ASC, Sid1 ASC) AS Row, *\r\n  FROM\r\n  (\r\n\t  SELECT DISTINCT TOP (@p0) T1, Sid1, 1 AS IsMatch, 0 AS IsPartial\r\n\t  FROM cte0\r\n\t  ORDER BY T1 ASC, Sid1 ASC\r\n  ) t\r\n)\r\nINSERT INTO @FilteredData SELECT T1, Sid1, IsMatch, IsPartial, Row FROM cte1\r\n;WITH cte1 AS (SELECT * FROM @FilteredData)\r\n,cte2 AS\r\n(\r\n  SELECT DISTINCT refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1, 0 AS IsMatch\r\n  FROM dbo.ReferenceSearchParam refSource\r\n\t   JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId\r\n  WHERE refSource.SearchParamId = 204\r\n\t  AND refTarget.IsHistory = 0\r\n\t  AND refTarget.IsDeleted = 0\r\n\t  AND refSource.ResourceTypeId IN (40)\r\n\t  AND EXISTS (SELECT * FROM cte1 WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1 AND Row < @p1)\r\n)\r\n,cte3 AS\r\n(\r\n  SELECT DISTINCT T1, Sid1, IsMatch, 0 AS IsPartial\r\n  FROM cte2\r\n)\r\n,cte4 AS\r\n(\r\n  SELECT DISTINCT refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1, 0 AS IsMatch\r\n  FROM dbo.ReferenceSearchParam refSource\r\n\t   JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId\r\n  WHERE refSource.SearchParamId = 404\r\n\t  AND refTarget.IsHistory = 0\r\n\t  AND refTarget.IsDeleted = 0\r\n\t  AND refSource.ResourceTypeId IN (40)\r\n\t  AND EXISTS (SELECT * FROM cte1 WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1)\r\n)\r\n,cte5 AS\r\n(\r\n  SELECT DISTINCT T1, Sid1, IsMatch, 0 AS IsPartial\r\n  FROM cte4\r\n)\r\n,cte6 AS\r\n(\r\n  SELECT DISTINCT refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1, 0 AS IsMatch\r\n  FROM dbo.ReferenceSearchParam refSource\r\n\t   JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId\r\n  WHERE refSource.SearchParamId = 204\r\n\t  AND refTarget.IsHistory = 0\r\n\t  AND refTarget.IsDeleted = 0\r\n\t  AND refSource.ResourceTypeId IN (124)\r\n\t  AND EXISTS (SELECT * FROM cte5 WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1)\r\n)\r\n,cte7 AS\r\n(\r\n  SELECT DISTINCT T1, Sid1, IsMatch, 0 AS IsPartial\r\n  FROM cte6\r\n)\r\n,cte10 AS\r\n(\r\n  SELECT DISTINCT refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1, 0 AS IsMatch\r\n  FROM dbo.ReferenceSearchParam refSource\r\n\t   JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId\r\n  WHERE refSource.SearchParamId = 470\r\n\t  AND refTarget.IsHistory = 0\r\n\t  AND refTarget.IsDeleted = 0\r\n\t  AND refSource.ResourceTypeId IN (44)\r\n\t  AND EXISTS (SELECT * FROM cte7 WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1 UNION SELECT * FROM cte3 WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1)\r\n)\r\n,cte11 AS\r\n(\r\n  SELECT DISTINCT T1, Sid1, IsMatch, 0 AS IsPartial\r\n  FROM cte10\r\n)\r\n,cte14 AS\r\n(\r\n  SELECT DISTINCT refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1, 0 AS IsMatch\r\n  FROM dbo.ReferenceSearchParam refSource\r\n\t   JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId\r\n  WHERE refSource.SearchParamId = 770\r\n\t  AND refTarget.IsHistory = 0\r\n\t  AND refTarget.IsDeleted = 0\r\n\t  AND refSource.ResourceTypeId IN (71)\r\n\t  AND EXISTS (SELECT * FROM cte11 WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1)\r\n)\r\n,cte15 AS\r\n(\r\n  SELECT DISTINCT T1, Sid1, IsMatch, 0 AS IsPartial\r\n  FROM cte14\r\n)\r\n,cte16 AS\r\n(\r\n  SELECT T1, Sid1, IsMatch, IsPartial\r\n  FROM cte1\r\n  UNION ALL\r\n  SELECT T1, Sid1, IsMatch, IsPartial\r\n  FROM cte3 WHERE NOT EXISTS (SELECT * FROM cte1 WHERE cte1.Sid1 = cte3.Sid1 AND cte1.T1 = cte3.T1)\r\n  UNION ALL\r\n  SELECT T1, Sid1, IsMatch, IsPartial\r\n  FROM cte5 WHERE NOT EXISTS (SELECT * FROM cte1 WHERE cte1.Sid1 = cte5.Sid1 AND cte1.T1 = cte5.T1)\r\n  UNION ALL\r\n  SELECT T1, Sid1, IsMatch, IsPartial\r\n  FROM cte7 WHERE NOT EXISTS (SELECT * FROM cte1 WHERE cte1.Sid1 = cte7.Sid1 AND cte1.T1 = cte7.T1)\r\n  UNION ALL\r\n  SELECT T1, Sid1, IsMatch, IsPartial\r\n  FROM cte11 WHERE NOT EXISTS (SELECT * FROM cte1 WHERE cte1.Sid1 = cte11.Sid1 AND cte1.T1 = cte11.T1)\r\n  UNION ALL\r\n  SELECT T1, Sid1, IsMatch, IsPartial\r\n  FROM cte15 WHERE NOT EXISTS (SELECT * FROM cte1 WHERE cte1.Sid1 = cte15.Sid1 AND cte1.T1 = cte15.T1)\r\n)\r\nSELECT DISTINCT r.ResourceTypeId, r.ResourceId, r.Version, r.IsDeleted, r.ResourceSurrogateId, r.RequestMethod, CAST(IsMatch AS bit) AS IsMatch, CAST(IsPartial AS bit) AS IsPartial, r.IsRawResourceMetaSet, r.SearchParamHash, r.RawResource\r\nFROM dbo.Resource r\r\n   JOIN cte16 ON r.ResourceTypeId = cte16.T1 AND r.ResourceSurrogateId = cte16.Sid1\r\nWHERE IsHistory = 0\r\n  AND IsDeleted = 0\r\nORDER BY IsMatch DESC, r.ResourceTypeId ASC, r.ResourceSurrogateId ASC\r\n\r\nOPTION (RECOMPILE)\r\n-- execution timeout = 180 sec.";

        private readonly string _iterativeIncludeUnchangedQuery = "DECLARE @p0 int = 11\r\nDECLARE @p1 int = 11\r\n\r\nSET STATISTICS IO ON;\r\nSET STATISTICS TIME ON;\r\n\r\nDECLARE @FilteredData AS TABLE (T1 smallint, Sid1 bigint, IsMatch bit, IsPartial bit, Row int)\r\n;WITH\r\ncte0 AS\r\n(\r\n  SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\r\n  FROM dbo.Resource\r\n  WHERE IsHistory = 0\r\n\t  AND IsDeleted = 0\r\n\t  AND ResourceTypeId = 40\r\n)\r\n,cte1 AS\r\n(\r\n  SELECT row_number() OVER (ORDER BY T1 ASC, Sid1 ASC) AS Row, *\r\n  FROM\r\n  (\r\n\t  SELECT DISTINCT TOP (@p0) T1, Sid1, 1 AS IsMatch, 0 AS IsPartial\r\n\t  FROM cte0\r\n\t  ORDER BY T1 ASC, Sid1 ASC\r\n  ) t\r\n)\r\nINSERT INTO @FilteredData SELECT T1, Sid1, IsMatch, IsPartial, Row FROM cte1\r\n;WITH cte1 AS (SELECT * FROM @FilteredData)\r\n,cte2 AS\r\n(\r\n  SELECT DISTINCT refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1, 0 AS IsMatch\r\n  FROM dbo.ReferenceSearchParam refSource\r\n\t   JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId\r\n  WHERE refSource.SearchParamId = 204\r\n\t  AND refTarget.IsHistory = 0\r\n\t  AND refTarget.IsDeleted = 0\r\n\t  AND refSource.ResourceTypeId IN (40)\r\n\t  AND EXISTS (SELECT * FROM cte1 WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1 AND Row < @p1)\r\n)\r\n,cte3 AS\r\n(\r\n  SELECT DISTINCT T1, Sid1, IsMatch, 0 AS IsPartial\r\n  FROM cte2\r\n)\r\n,cte4 AS\r\n(\r\n  SELECT DISTINCT refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1, 0 AS IsMatch\r\n  FROM dbo.ReferenceSearchParam refSource\r\n\t   JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId\r\n  WHERE refSource.SearchParamId = 404\r\n\t  AND refTarget.IsHistory = 0\r\n\t  AND refTarget.IsDeleted = 0\r\n\t  AND refSource.ResourceTypeId IN (40)\r\n\t  AND EXISTS (SELECT * FROM cte1 WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1)\r\n)\r\n,cte5 AS\r\n(\r\n  SELECT DISTINCT T1, Sid1, IsMatch, 0 AS IsPartial\r\n  FROM cte4\r\n)\r\n,cte6 AS\r\n(\r\n  SELECT DISTINCT refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1, 0 AS IsMatch\r\n  FROM dbo.ReferenceSearchParam refSource\r\n\t   JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId\r\n  WHERE refSource.SearchParamId = 470\r\n\t  AND refTarget.IsHistory = 0\r\n\t  AND refTarget.IsDeleted = 0\r\n\t  AND refSource.ResourceTypeId IN (44)\r\n\t  AND EXISTS (SELECT * FROM cte3 WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1)\r\n)\r\n,cte7 AS\r\n(\r\n  SELECT DISTINCT T1, Sid1, IsMatch, 0 AS IsPartial\r\n  FROM cte6\r\n)\r\n,cte8 AS\r\n(\r\n  SELECT DISTINCT refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1, 0 AS IsMatch\r\n  FROM dbo.ReferenceSearchParam refSource\r\n\t   JOIN dbo.Resource refTarget ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId AND refSource.ReferenceResourceId = refTarget.ResourceId\r\n  WHERE refSource.SearchParamId = 770\r\n\t  AND refTarget.IsHistory = 0\r\n\t  AND refTarget.IsDeleted = 0\r\n\t  AND refSource.ResourceTypeId IN (71)\r\n\t  AND EXISTS (SELECT * FROM cte7 WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1)\r\n)\r\n,cte9 AS\r\n(\r\n  SELECT DISTINCT T1, Sid1, IsMatch, 0 AS IsPartial\r\n  FROM cte8\r\n)\r\n,cte10 AS\r\n(\r\n  SELECT T1, Sid1, IsMatch, IsPartial\r\n  FROM cte1\r\n  UNION ALL\r\n  SELECT T1, Sid1, IsMatch, IsPartial\r\n  FROM cte3 WHERE NOT EXISTS (SELECT * FROM cte1 WHERE cte1.Sid1 = cte3.Sid1 AND cte1.T1 = cte3.T1)\r\n  UNION ALL\r\n  SELECT T1, Sid1, IsMatch, IsPartial\r\n  FROM cte5 WHERE NOT EXISTS (SELECT * FROM cte1 WHERE cte1.Sid1 = cte5.Sid1 AND cte1.T1 = cte5.T1)\r\n  UNION ALL\r\n  SELECT T1, Sid1, IsMatch, IsPartial\r\n  FROM cte7 WHERE NOT EXISTS (SELECT * FROM cte1 WHERE cte1.Sid1 = cte7.Sid1 AND cte1.T1 = cte7.T1)\r\n  UNION ALL\r\n  SELECT T1, Sid1, IsMatch, IsPartial\r\n  FROM cte9 WHERE NOT EXISTS (SELECT * FROM cte1 WHERE cte1.Sid1 = cte9.Sid1 AND cte1.T1 = cte9.T1)\r\n)\r\nSELECT DISTINCT r.ResourceTypeId, r.ResourceId, r.Version, r.IsDeleted, r.ResourceSurrogateId, r.RequestMethod, CAST(IsMatch AS bit) AS IsMatch, CAST(IsPartial AS bit) AS IsPartial, r.IsRawResourceMetaSet, r.SearchParamHash, r.RawResource\r\nFROM dbo.Resource r\r\n   JOIN cte10 ON r.ResourceTypeId = cte10.T1 AND r.ResourceSurrogateId = cte10.Sid1\r\nWHERE IsHistory = 0\r\n  AND IsDeleted = 0\r\nORDER BY IsMatch DESC, r.ResourceTypeId ASC, r.ResourceSurrogateId ASC\r\n\r\nOPTION (RECOMPILE)\r\n-- execution timeout = 180 sec.";

        public SqlCommandSimplifierTests()
        {
            _logger = Substitute.For<ILogger>();
        }

        [Fact]
        public void GivenACommandWithDistinct_WhenSimplified_ThenTheDistinctIsRemoved()
        {
            string startingString = "select distinct * from Resource where Resource.IsDeleted = 0 and Resource.IsHistory = 1 order by Resource.ResourceSurrogateId desc";
            string expectedString = "select * from Resource where Resource.IsDeleted = 0 and Resource.IsHistory = 1 order by Resource.ResourceSurrogateId desc";
            IndentedStringBuilder stringBuilder = new IndentedStringBuilder(new StringBuilder(startingString));
            SqlParameterCollection sqlParameterCollection = RuntimeHelpers.GetUninitializedObject(typeof(SqlParameterCollection)) as SqlParameterCollection;
            SqlCommandSimplifier.RemoveRedundantParameters(stringBuilder, sqlParameterCollection, _logger);
            string actualString = stringBuilder.ToString();
            Assert.Equal(expectedString, actualString);
        }

        [Fact]
        public void GivenACommandWithRedundantConditions_WhenSimplified_ThenOnlyOneConditionIsLeft()
        {
            string startingString = "select distinct * from Resource where Resource.ResourceSurrogateId >= @p1 and Resource.ResourceSurrogateId > @p2 and Resource.ResourceSurrogateId <= @p3 and Resource.ResourceSurrogateId < @p4 and Resource.IsDeleted = 0 and Resource.IsHistory = 1 order by Resource.ResourceSurrogateId desc";
            string expectedString = "select * from Resource where Resource.ResourceSurrogateId >= @p1 and 1 = 1 and 1 = 1 and Resource.ResourceSurrogateId < @p4 and Resource.IsDeleted = 0 and Resource.IsHistory = 1 order by Resource.ResourceSurrogateId desc";
            IndentedStringBuilder stringBuilder = new IndentedStringBuilder(new StringBuilder(startingString));
            SqlParameterCollection sqlParameterCollection = RuntimeHelpers.GetUninitializedObject(typeof(SqlParameterCollection)) as SqlParameterCollection;
            sqlParameterCollection.Add(new SqlParameter("@p1", 5L));
            sqlParameterCollection.Add(new SqlParameter("@p2", 4L));
            sqlParameterCollection.Add(new SqlParameter("@p3", 13L));
            sqlParameterCollection.Add(new SqlParameter("@p4", 11L));
            SqlCommandSimplifier.RemoveRedundantParameters(stringBuilder, sqlParameterCollection, _logger);
            string actualString = stringBuilder.ToString();
            Assert.Equal(expectedString, actualString);
        }

        [Fact]
        public void GivenACommandWithCTEs_WhenSimplified_ThenNothingIsChanged()
        {
            string startingString = "select distinct * from Resource where cte Resource.IsDeleted = 0 and Resource.IsHistory = 1 order by Resource.ResourceSurrogateId desc";
            string expectedString = "select distinct * from Resource where cte Resource.IsDeleted = 0 and Resource.IsHistory = 1 order by Resource.ResourceSurrogateId desc";
            IndentedStringBuilder stringBuilder = new IndentedStringBuilder(new StringBuilder(startingString));
            SqlParameterCollection sqlParameterCollection = RuntimeHelpers.GetUninitializedObject(typeof(SqlParameterCollection)) as SqlParameterCollection;
            SqlCommandSimplifier.RemoveRedundantParameters(stringBuilder, sqlParameterCollection, _logger);
            string actualString = stringBuilder.ToString();
            Assert.Equal(expectedString, actualString);
        }

        [Fact]
        public void GivenACommandWithOrConditions_WhenSimplified_ThenConditionsAreNotChanged()
        {
            string startingString = "select distinct * from Resource where Resource.ResourceSurrogateId >= @p1 and Resource.ResourceSurrogateId > @p2 and Resource.ResourceSurrogateId <= @p3 and Resource.ResourceSurrogateId < @p4 and Resource.IsDeleted = 0 or Resource.IsHistory = 1 order by Resource.ResourceSurrogateId desc";
            string expectedString = "select * from Resource where Resource.ResourceSurrogateId >= @p1 and Resource.ResourceSurrogateId > @p2 and Resource.ResourceSurrogateId <= @p3 and Resource.ResourceSurrogateId < @p4 and Resource.IsDeleted = 0 or Resource.IsHistory = 1 order by Resource.ResourceSurrogateId desc";
            IndentedStringBuilder stringBuilder = new IndentedStringBuilder(new StringBuilder(startingString));
            SqlParameterCollection sqlParameterCollection = RuntimeHelpers.GetUninitializedObject(typeof(SqlParameterCollection)) as SqlParameterCollection;
            sqlParameterCollection.Add(new SqlParameter("@p1", 5L));
            sqlParameterCollection.Add(new SqlParameter("@p2", 4L));
            sqlParameterCollection.Add(new SqlParameter("@p3", 13L));
            sqlParameterCollection.Add(new SqlParameter("@p4", 11L));
            SqlCommandSimplifier.RemoveRedundantParameters(stringBuilder, sqlParameterCollection, _logger);
            string actualString = stringBuilder.ToString();
            Assert.Equal(expectedString, actualString);
        }

        [Fact]
        public void GivenACommandThatFailsSimplification_WhenSimplified_ThenNothingIsChanged()
        {
            string startingString = "select distinct * from Resource where Resource.ResourceSurrogateId >= @p1 and Resource.ResourceSurrogateId > @p2 and Resource.IsDeleted = 0 and Resource.IsHistory = 1 order by Resource.ResourceSurrogateId desc";
            string expectedString = "select distinct * from Resource where Resource.ResourceSurrogateId >= @p1 and Resource.ResourceSurrogateId > @p2 and Resource.IsDeleted = 0 and Resource.IsHistory = 1 order by Resource.ResourceSurrogateId desc";
            IndentedStringBuilder stringBuilder = new IndentedStringBuilder(new StringBuilder(startingString));
            SqlParameterCollection sqlParameterCollection = RuntimeHelpers.GetUninitializedObject(typeof(SqlParameterCollection)) as SqlParameterCollection;

            // The simplifier expects surrogate ids to be longs.
            sqlParameterCollection.Add(new SqlParameter("@p1", "test"));
            sqlParameterCollection.Add(new SqlParameter("@p2", 4L));
            SqlCommandSimplifier.RemoveRedundantParameters(stringBuilder, sqlParameterCollection, _logger);
            string actualString = stringBuilder.ToString();
            Assert.Equal(expectedString, actualString);
        }

        [Fact]
        public void GivenACommandThatContainsIterativeIncludesWithSharedTypes_WhenSimplified_ThenCtesAreUnioned()
        {
            string startingString = _iterativeIncludeStartingQuery;
            string expectedString = _iterativeIncludeEndingQuery;

            IndentedStringBuilder stringBuilder = new IndentedStringBuilder(new StringBuilder(startingString));

            SqlCommandSimplifier.CombineIterativeIncludes(stringBuilder, _logger);

            string actualString = stringBuilder.ToString();
            Assert.Equal(expectedString, actualString);
        }

        [Fact]
        public void GivenACommandThatContainsIterativeIncludesWithoutSharedTypes_WhenSimplified_ThenNothingIsChanged()
        {
            string startingString = _iterativeIncludeUnchangedQuery;
            string expectedString = _iterativeIncludeUnchangedQuery;

            IndentedStringBuilder stringBuilder = new IndentedStringBuilder(new StringBuilder(startingString));

            SqlCommandSimplifier.CombineIterativeIncludes(stringBuilder, _logger);

            string actualString = stringBuilder.ToString();
            Assert.Equal(expectedString, actualString);
        }
    }
}
