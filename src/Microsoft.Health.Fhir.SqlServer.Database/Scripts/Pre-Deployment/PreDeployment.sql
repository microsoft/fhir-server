/*
 REAL Pre-Deployment Script
--------------------------------------------------------------------------------------
 Visual Studio is capable to run a pre-deployment script when it has 'PreDeploy' Build Action.
 Unfortunately, this happens after schema analysis phase of sqlpackage.exe.

 As a result, it is impossible to make certain updates using sqlpackage.exe.
 E.g. adding non-nullable column to a non-emtpy table.

 Do such changes below. They will be applyed before calling sqlpackage.exe.
 --------------------------------------------------------------------------------------
*/
set nocount on
GO
SET QUOTED_IDENTIFIER ON
GO
--DECLARE @db varchar(100) = db_name()
--IF EXISTS (SELECT * FROM sys.databases WHERE name = @db AND compatibility_level > 130)
--  EXECUTE('ALTER DATABASE ['+@db+'] SET COMPATIBILITY_LEVEL = 130')
GO
IF object_id('dbo.NotExistingProcedure') IS NOT NULL DROP PROCEDURE dbo.NotExistingProcedure
GO
