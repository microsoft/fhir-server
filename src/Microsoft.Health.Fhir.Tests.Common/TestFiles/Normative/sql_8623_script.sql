DECLARE @p0 SmallInt = 1418;
      DECLARE @p1 VarChar(256) = '1';
      DECLARE @p2 SmallInt = 103;
      DECLARE @p3 SmallInt = 1016;
      DECLARE @p4 NVarChar(256) = N'Patricia';
      DECLARE @p5 NVarChar(256) = N'Ann';
      DECLARE @p6 NVarChar(256) = N'Person';
      DECLARE @p7 SmallInt = 696;
      DECLARE @p8 SmallInt = 694;
      DECLARE @p9 SmallInt = 1013;
      DECLARE @p10 NVarChar(256) = N'http://example.org/old-payer/identifiers/member';
      DECLARE @p11 VarChar(256) = '55678';
      DECLARE @p12 SmallInt = 1419;
      DECLARE @p13 NVarChar(256) = N'(http://example.org/old-payer/identifiers/member';
      DECLARE @p14 VarChar(256) = '55678) ';
      DECLARE @p15 NVarChar(256) = N' (Person)%';
      DECLARE @p16 SmallInt = 690;
      DECLARE @p17 DateTime2 = '1974-12-25T00:00:00.0000000Z';
      DECLARE @p18 DateTime2 = '1974-12-25T23:59:59.9999999Z';
      DECLARE @p19 SmallInt = 1011;
      DECLARE @p20 Int = 9;
      DECLARE @p21 VarChar(256) = 'false';
      DECLARE @p22 SmallInt = 692;
      DECLARE @p23 SmallInt = 693;
      DECLARE @p24 Int = 12;
      DECLARE @p25 VarChar(256) = 'female';
      DECLARE @p26 SmallInt = 336;
      DECLARE @p27 SmallInt = 31;
      DECLARE @p28 SmallInt = 338;
      DECLARE @p29 NVarChar(256) = N'CB135';
      DECLARE @p30 NVarChar(256) = N'B37FC';
      DECLARE @p31 NVarChar(256) = N'P7';
      DECLARE @p32 NVarChar(256) = N'SILVER';
      DECLARE @p33 SmallInt = 337;
      DECLARE @p34 NVarChar(256) = N'http://terminology.hl7.org/CodeSystem/coverage-class';
      DECLARE @p35 VarChar(256) = 'group';
      DECLARE @p36 VarChar(256) = 'plan';
      DECLARE @p37 VarChar(256) = 'subplan';
      DECLARE @p38 VarChar(256) = 'class';
      DECLARE @p39 VarChar(256) = '9876B1';
      DECLARE @p40 SmallInt = 356;
      DECLARE @p41 NVarChar(256) = N'http://example.org/old-payer';
      DECLARE @p42 VarChar(256) = 'DH10001235';
      DECLARE @p43 SmallInt = 360;
      DECLARE @p44 NVarChar(256) = N'http://hl7.org/fhir/fm-status';
      DECLARE @p45 VarChar(256) = 'draft';
      DECLARE @p46 Int = 3;

      SET STATISTICS IO ON;
      SET STATISTICS TIME ON;

      WITH cte0 AS
      (
          SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
          FROM dbo.TokenSearchParam
          WHERE IsHistory = 0
              AND SearchParamId = @p0
              AND Code = @p1
              AND ResourceTypeId = @p2

      ),
      cte1 AS
      (
          SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
          FROM dbo.StringSearchParam
          WHERE IsHistory = 0
              AND EXISTS(SELECT * FROM cte0 WHERE ResourceTypeId = T1 AND ResourceSurrogateId = Sid1)
              AND SearchParamId = @p3
              AND Text = @p4 AND Text = @p4 COLLATE Latin1_General_100_CS_AS
              AND ResourceTypeId = @p2

      ),
      cte2 AS
      (
          SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
          FROM dbo.StringSearchParam
          WHERE IsHistory = 0
              AND EXISTS(SELECT * FROM cte1 WHERE ResourceTypeId = T1 AND ResourceSurrogateId = Sid1)
              AND SearchParamId = @p3
              AND Text = @p5 AND Text = @p5 COLLATE Latin1_General_100_CS_AS
              AND ResourceTypeId = @p2

      ),
      cte3 AS
      (
          SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
          FROM dbo.StringSearchParam
          WHERE IsHistory = 0
              AND EXISTS(SELECT * FROM cte2 WHERE ResourceTypeId = T1 AND ResourceSurrogateId = Sid1)
              AND SearchParamId = @p3
              AND Text = @p6 AND Text = @p6 COLLATE Latin1_General_100_CS_AS
              AND ResourceTypeId = @p2

      ),
      cte4 AS
      (
          SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
          FROM dbo.StringSearchParam
          WHERE IsHistory = 0
              AND EXISTS(SELECT * FROM cte3 WHERE ResourceTypeId = T1 AND ResourceSurrogateId = Sid1)
              AND SearchParamId = @p7
              AND Text = @p4 AND Text = @p4 COLLATE Latin1_General_100_CS_AS
              AND ResourceTypeId = @p2

      ),
      cte5 AS
      (
          SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
          FROM dbo.StringSearchParam
          WHERE IsHistory = 0
              AND EXISTS(SELECT * FROM cte4 WHERE ResourceTypeId = T1 AND ResourceSurrogateId = Sid1)
              AND SearchParamId = @p7
              AND Text = @p5 AND Text = @p5 COLLATE Latin1_General_100_CS_AS
              AND ResourceTypeId = @p2

      ),
      cte6 AS
      (
          SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
          FROM dbo.StringSearchParam
          WHERE IsHistory = 0
              AND EXISTS(SELECT * FROM cte5 WHERE ResourceTypeId = T1 AND ResourceSurrogateId = Sid1)
              AND SearchParamId = @p7
              AND Text = @p6 AND Text = @p6 COLLATE Latin1_General_100_CS_AS
              AND ResourceTypeId = @p2

      ),
      cte7 AS
      (
          SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
          FROM dbo.StringSearchParam
          WHERE IsHistory = 0
              AND EXISTS(SELECT * FROM cte6 WHERE ResourceTypeId = T1 AND ResourceSurrogateId = Sid1)
              AND SearchParamId = @p8
              AND Text = @p4 AND Text = @p4 COLLATE Latin1_General_100_CS_AS
              AND ResourceTypeId = @p2

      ),
      cte8 AS
      (
          SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
          FROM dbo.StringSearchParam
          WHERE IsHistory = 0
              AND EXISTS(SELECT * FROM cte7 WHERE ResourceTypeId = T1 AND ResourceSurrogateId = Sid1)
              AND SearchParamId = @p8
              AND Text = @p5 AND Text = @p5 COLLATE Latin1_General_100_CS_AS
              AND ResourceTypeId = @p2

      ),
      cte9 AS
      (
          SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
          FROM dbo.TokenSearchParam
          WHERE IsHistory = 0
              AND EXISTS(SELECT * FROM cte8 WHERE ResourceTypeId = T1 AND ResourceSurrogateId = Sid1)
              AND SearchParamId = @p9
              AND SystemId IN (SELECT SystemId FROM dbo.System WHERE Value = @p10)
              AND Code = @p11

              AND ResourceTypeId = @p2

      ),
      cte10 AS
      (
          SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
          FROM dbo.TokenStringCompositeSearchParam
          WHERE IsHistory = 0
              AND EXISTS(SELECT * FROM cte9 WHERE ResourceTypeId = T1 AND ResourceSurrogateId = Sid1)
              AND SearchParamId = @p12
              AND SystemId1 IN (SELECT SystemId FROM dbo.System WHERE Value = @p13)
              AND Code1 = @p14
              AND Text2 LIKE @p15

              AND ResourceTypeId = @p2

      ),
      cte11 AS
      (
          SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
          FROM dbo.DateTimeSearchParam
          WHERE IsHistory = 0
              AND EXISTS(SELECT * FROM cte10 WHERE ResourceTypeId = T1 AND ResourceSurrogateId = Sid1)
              AND SearchParamId = @p16
              AND StartDateTime >= @p17
              AND StartDateTime <= @p18
              AND EndDateTime <= @p18

              AND ResourceTypeId = @p2

      ),
      cte12 AS
      (
          SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
          FROM dbo.TokenSearchParam
          WHERE IsHistory = 0
              AND EXISTS(SELECT * FROM cte11 WHERE ResourceTypeId = T1 AND ResourceSurrogateId = Sid1)
              AND SearchParamId = @p19
              AND SystemId = @p20
              AND Code = @p21

              AND ResourceTypeId = @p2

      ),
      cte13 AS
      (
          SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
          FROM dbo.StringSearchParam
          WHERE IsHistory = 0
              AND EXISTS(SELECT * FROM cte12 WHERE ResourceTypeId = T1 AND ResourceSurrogateId = Sid1)
              AND SearchParamId = @p22
              AND Text = @p6 AND Text = @p6 COLLATE Latin1_General_100_CS_AS
              AND ResourceTypeId = @p2

      ),
      cte14 AS
      (
          SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
          FROM dbo.TokenSearchParam
          WHERE IsHistory = 0
              AND EXISTS(SELECT * FROM cte13 WHERE ResourceTypeId = T1 AND ResourceSurrogateId = Sid1)
              AND SearchParamId = @p23
              AND SystemId = @p24
              AND Code = @p25

              AND ResourceTypeId = @p2

      ),
      cte15 AS
      (
          SELECT refSource.ResourceTypeId AS T2, refSource.ResourceSurrogateId AS Sid2, refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1
          FROM dbo.ReferenceSearchParam refSource
          INNER JOIN dbo.Resource refTarget
          ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId
              AND refSource.ReferenceResourceId = refTarget.ResourceId
          WHERE refSource.SearchParamId = @p26
              AND refTarget.IsHistory = 0
              AND refSource.IsHistory = 0
              AND refSource.ResourceTypeId IN (@p27)
              AND refSource.ReferenceResourceTypeId IN (@p2)
              AND EXISTS(SELECT * FROM cte14 WHERE refTarget.ResourceTypeId = T1 AND refTarget.ResourceSurrogateId = Sid1)
              AND refTarget.ResourceTypeId = @p2
      ),
      cte16 AS
      (
          SELECT T1, Sid1, ResourceTypeId AS T2,
          ResourceSurrogateId AS Sid2
          FROM dbo.StringSearchParam
          INNER JOIN cte15
          ON ResourceTypeId = T2
              AND ResourceSurrogateId = Sid2
          WHERE IsHistory = 0
              AND SearchParamId = @p28
              AND Text = @p29 AND Text = @p29 COLLATE Latin1_General_100_CS_AS
      ),
      cte17 AS
      (
          SELECT T1, Sid1, ResourceTypeId AS T2,
          ResourceSurrogateId AS Sid2
          FROM dbo.StringSearchParam
          INNER JOIN cte16
          ON ResourceTypeId = T2
              AND ResourceSurrogateId = Sid2
          WHERE IsHistory = 0
              AND SearchParamId = @p28
              AND Text = @p30 AND Text = @p30 COLLATE Latin1_General_100_CS_AS
      ),
      cte18 AS
      (
          SELECT T1, Sid1, ResourceTypeId AS T2,
          ResourceSurrogateId AS Sid2
          FROM dbo.StringSearchParam
          INNER JOIN cte17
          ON ResourceTypeId = T2
              AND ResourceSurrogateId = Sid2
          WHERE IsHistory = 0
              AND SearchParamId = @p28
              AND Text = @p31 AND Text = @p31 COLLATE Latin1_General_100_CS_AS
      ),
      cte19 AS
      (
          SELECT T1, Sid1, ResourceTypeId AS T2,
          ResourceSurrogateId AS Sid2
          FROM dbo.StringSearchParam
          INNER JOIN cte18
          ON ResourceTypeId = T2
              AND ResourceSurrogateId = Sid2
          WHERE IsHistory = 0
              AND SearchParamId = @p28
              AND Text = @p32 AND Text = @p32 COLLATE Latin1_General_100_CS_AS
      ),
      cte20 AS
      (
          SELECT T1, Sid1, ResourceTypeId AS T2,
          ResourceSurrogateId AS Sid2
          FROM dbo.TokenSearchParam
          INNER JOIN cte19
          ON ResourceTypeId = T2
              AND ResourceSurrogateId = Sid2
          WHERE IsHistory = 0
              AND SearchParamId = @p33
              AND SystemId IN (SELECT SystemId FROM dbo.System WHERE Value = @p34)
              AND Code = @p35

      ),
      cte21 AS
      (
          SELECT T1, Sid1, ResourceTypeId AS T2,
          ResourceSurrogateId AS Sid2
          FROM dbo.TokenSearchParam
          INNER JOIN cte20
          ON ResourceTypeId = T2
              AND ResourceSurrogateId = Sid2
          WHERE IsHistory = 0
              AND SearchParamId = @p33
              AND SystemId IN (SELECT SystemId FROM dbo.System WHERE Value = @p34)
              AND Code = @p36

      ),
      cte22 AS
      (
          SELECT T1, Sid1, ResourceTypeId AS T2,
          ResourceSurrogateId AS Sid2
          FROM dbo.TokenSearchParam
          INNER JOIN cte21
          ON ResourceTypeId = T2
              AND ResourceSurrogateId = Sid2
          WHERE IsHistory = 0
              AND SearchParamId = @p33
              AND SystemId IN (SELECT SystemId FROM dbo.System WHERE Value = @p34)
              AND Code = @p37

      ),
      cte23 AS
      (
          SELECT T1, Sid1, ResourceTypeId AS T2,
          ResourceSurrogateId AS Sid2
          FROM dbo.TokenSearchParam
          INNER JOIN cte22
          ON ResourceTypeId = T2
              AND ResourceSurrogateId = Sid2
          WHERE IsHistory = 0
              AND SearchParamId = @p33
              AND SystemId IN (SELECT SystemId FROM dbo.System WHERE Value = @p34)
              AND Code = @p38

      ),
      cte24 AS
      (
          SELECT T1, Sid1, ResourceTypeId AS T2,
          ResourceSurrogateId AS Sid2
          FROM dbo.TokenSearchParam
          INNER JOIN cte23
          ON ResourceTypeId = T2
              AND ResourceSurrogateId = Sid2
          WHERE IsHistory = 0
              AND SearchParamId = @p0
              AND Code = @p39
      ),
      cte25 AS
      (
          SELECT T1, Sid1, ResourceTypeId AS T2,
          ResourceSurrogateId AS Sid2
          FROM dbo.TokenSearchParam
          INNER JOIN cte24
          ON ResourceTypeId = T2
              AND ResourceSurrogateId = Sid2
          WHERE IsHistory = 0
              AND SearchParamId = @p40
              AND SystemId IN (SELECT SystemId FROM dbo.System WHERE Value = @p41)
              AND Code = @p42

      ),
      cte26 AS
      (
          SELECT T1, Sid1, ResourceTypeId AS T2,
          ResourceSurrogateId AS Sid2
          FROM dbo.TokenSearchParam
          INNER JOIN cte25
          ON ResourceTypeId = T2
              AND ResourceSurrogateId = Sid2
          WHERE IsHistory = 0
              AND SearchParamId = @p43
              AND SystemId IN (SELECT SystemId FROM dbo.System WHERE Value = @p44)
              AND Code = @p45

      ),
      cte27 AS
      (
          SELECT DISTINCT TOP (@p46) T1, Sid1, 1 AS IsMatch, 0 AS IsPartial
          FROM cte26
          ORDER BY T1 ASC, Sid1 ASC
      )
      SELECT DISTINCT r.ResourceTypeId, r.ResourceId, r.Version, r.IsDeleted, r.ResourceSurrogateId, r.RequestMethod, CAST(IsMatch AS bit) AS IsMatch, CAST(IsPartial AS bit) AS IsPartial, r.IsRawResourceMetaSet, r.SearchParamHash, r.RawResource
      FROM dbo.Resource r

      INNER JOIN cte27
      ON r.ResourceTypeId = cte27.T1 AND
      r.ResourceSurrogateId = cte27.Sid1
      ORDER BY r.ResourceTypeId ASC, r.ResourceSurrogateId ASC
      /* HASH ZgZSXBM+MzloBRd89dLWBC3ztlqMo2OIDo5Jt9leEy8= */
