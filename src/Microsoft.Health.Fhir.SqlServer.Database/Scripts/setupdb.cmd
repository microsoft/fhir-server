@echo off
echo.
echo *** Starting setupdb.cmd (Server=%1 Database=%2 User=%3 Pwd=%4)...

set Server=%1
set Database=%2
set User=%3
set Pwd=%4

set StoreConnectionString=server=%Server%;database=%Database%
if "%User%" == "" set StoreConnectionString="%StoreConnectionString%;integrated security=true"
if "%User%" neq "" set StoreConnectionString="%StoreConnectionString%;user=%User%;pwd=%Pwd%"
if "%User%" == "" set SecurityLine=/E
if "%User%" neq "" set SecurityLine=/U%User% /P%Pwd%

echo *** Creating database if not exists...
echo Running: sqlcmd.exe /S%Server% /dmaster %SecurityLine% /b /h-1 /Q"EXIT(set nocount on DECLARE @Exists bit = CASE WHEN EXISTS (SELECT * FROM sys.databases WHERE name = '%Database%') THEN 1 ELSE 0 END IF @Exists = 1 SELECT 0 ELSE SELECT 100)"
sqlcmd.exe /S%Server% /dmaster %SecurityLine% /b /h-1 /Q"EXIT(set nocount on DECLARE @Exists bit = CASE WHEN EXISTS (SELECT * FROM sys.databases WHERE name = '%Database%') THEN 1 ELSE 0 END IF @Exists = 1 SELECT 0 ELSE SELECT 100)"
set errorlev=%errorlevel%
set DBExists=true
if "%errorlev%" == "100" set DBExists=false
echo %Database% database existed before setup = %DBExists% 
if "%errorlev%" == "100" goto afterErrorCheck
if not %errorlev% == 0 echo ERROR checking database existence && exit /B %errorlev%
:afterErrorCheck

if "%DBExists%" == "true" echo *** Skipping database create... && goto afterCreate
echo Running: sqlcmd.exe /S%Server% /dmaster %SecurityLine% /b /Q"CREATE DATABASE [%Database%]"
sqlcmd.exe /S%Server% /dmaster %SecurityLine% /b /Q"CREATE DATABASE [%Database%]"
if not %errorlevel% == 0 echo ERROR creating database if not exists && exit /B %errorlevel%
:afterCreate

echo *** Applying PreDeployment.sql...
rem Allow for long login timeout to wake up dormant database
sqlcmd.exe /S%Server% /d%Database% %SecurityLine% /b /l90 /i%~dp0Pre-Deployment\PreDeployment.sql
if not %errorlevel% == 0 echo ERROR executing PreDeployment.sql && exit /B %errorlevel%
echo *** PreDeployment.sql applied.

echo *** Deploying Microsoft.Health.Fhir.SqlServer.Database.dacpac...
echo Running: sqlpackage.exe /Action:Publish /SourceFile:"%~dp0..\Microsoft.Health.Fhir.SqlServer.Database.dacpac" /TargetConnectionString:%StoreConnectionString% /p:AllowDropBlockingAssemblies=true /p:NoAlterStatementsToChangeClrTypes=true /p:AllowIncompatiblePlatform=true /p:IncludeCompositeObjects=true
sqlpackage.exe /Action:Publish /SourceFile:"%~dp0..\Microsoft.Health.Fhir.SqlServer.Database.dacpac" /TargetConnectionString:%StoreConnectionString% /p:AllowDropBlockingAssemblies=true /p:NoAlterStatementsToChangeClrTypes=true /p:AllowIncompatiblePlatform=true /p:IncludeCompositeObjects=true
if not %errorlevel% == 0 echo ERROR deploying Microsoft.Health.Fhir.SqlServer.Database.dacpac && exit /B %errorlevel%
echo *** Microsoft.Health.Fhir.SqlServer.Database.dacpac deployed.

sqlcmd.exe /S%Server% /d%Database% %SecurityLine% /b /Q"EXECUTE dbo.LogEvent @Process='setupdb', @Status='Warn', @Text='Completed'"

echo *** setupdb.cmd (Server=%1 Database=%2) completed.
echo.
