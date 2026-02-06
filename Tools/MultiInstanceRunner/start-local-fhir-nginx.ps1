#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$SqlConnectionString = "Server=JE-WORKLENOVO;Database=FHIR_R4;Integrated Security=true;TrustServerCertificate=True",

    [int]$InstanceCount = 3,

    [int]$BasePort = 5001,

    [int]$NginxListenPort = 4343,

    [string]$ProjectPath = "src/Microsoft.Health.Fhir.R4.Web",

    [string]$NginxPath = "",

    [string]$NginxVersion = "1.26.2",

    [switch]$NoLaunchProfile,

    [switch]$SkipBuild,

    [string]$Framework = "net9.0",

    [string]$DatabaseName = "FHIR_R4",

    [switch]$SkipSchemaInitialization,

    [switch]$RedirectOutput,

    [switch]$DisableSecurity = $true,

    [switch]$PersistTestEnvironment
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------------------------
# Normalize SQL connection string (FIX)
# ---------------------------
function Normalize-SqlConnectionString {
    param(
        [Parameter(Mandatory=$true)][string]$ConnectionString,
        [Parameter(Mandatory=$false)][string]$DatabaseNameFallback
    )

    # Strip control chars (CR/LF/null/etc.) and quotes
    $raw = $ConnectionString.Trim().Trim('"')
    $raw = [regex]::Replace($raw, '[\x00-\x1F]', '')

    # Remove wrapper tokens if present
    $raw = $raw -replace '(?i)(^|;)\s*(ConnectionString|SqlServer:ConnectionString|DefaultConnection)\s*=\s*', '$1'

    $b = New-Object System.Data.SqlClient.SqlConnectionStringBuilder

    foreach ($part in ($raw -split ';')) {
        $p = $part.Trim()
        if ([string]::IsNullOrWhiteSpace($p)) { continue }

        $idx = $p.IndexOf('=')
        if ($idx -lt 1) { continue }

        $key = $p.Substring(0, $idx).Trim()
        $val = $p.Substring($idx + 1).Trim()

        switch -Regex ($key) {
            '^(?i)(server|data\s*source|addr|address|network\s*address)$' {
                $b["Data Source"] = $val
                continue
            }
            '^(?i)(database|initial\s*catalog|initialcatalog)$' {
                $b["Initial Catalog"] = $val
                continue
            }
            '^(?i)(integrated\s*security|trusted_connection)$' {
                $v = $val.ToLowerInvariant()
                $b["Integrated Security"] = ($v -in @('true','yes','sspi','1'))
                continue
            }
            '^(?i)(user\s*id|uid)$' { $b["User ID"] = $val; continue }
            '^(?i)(password|pwd)$' { $b["Password"] = $val; continue }
            '^(?i)trustservercertificate$' {
                $v = $val.ToLowerInvariant()
                $b["TrustServerCertificate"] = ($v -in @('true','yes','1'))
                continue
            }
            '^(?i)encrypt$' {
                $v = $val.ToLowerInvariant()
                $b["Encrypt"] = ($v -in @('true','yes','1'))
                continue
            }
            default { continue } # ignore garbage keys like ConnectionString
        }
    }

    if ([string]::IsNullOrWhiteSpace($b["Initial Catalog"])) {
        if ([string]::IsNullOrWhiteSpace($DatabaseNameFallback)) {
            throw "Connection string missing database (Initial Catalog/Database) and -DatabaseName is empty."
        }
        $b["Initial Catalog"] = $DatabaseNameFallback
    }

    if ([string]::IsNullOrWhiteSpace($b["Data Source"])) {
        throw "Connection string missing Server/Data Source. Value=[$ConnectionString]"
    }

    return $b.ConnectionString
}



$SqlConnectionString = Normalize-SqlConnectionString -ConnectionString $SqlConnectionString -DatabaseNameFallback $DatabaseName

# ---------------------------
# Paths / validation
# ---------------------------
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$projectFullPath = Join-Path $repoRoot $ProjectPath

if (-not (Test-Path $projectFullPath)) {
    throw "Project not found at '$projectFullPath'."
}

if ($InstanceCount -lt 1) {
    throw "InstanceCount must be >= 1."
}

# ---------------------------
# NGINX setup
# ---------------------------
$nginxExe = $NginxPath
if ([string]::IsNullOrWhiteSpace($nginxExe)) {
    $nginxCommand = Get-Command nginx -ErrorAction SilentlyContinue
    if ($null -ne $nginxCommand) {
        $nginxExe = $nginxCommand.Source
    }
}

$nginxRoot = Join-Path $repoRoot ".local\nginx"
New-Item -ItemType Directory -Force -Path $nginxRoot | Out-Null
$nginxLogs = Join-Path $nginxRoot "logs"
$nginxConfDir = Join-Path $nginxRoot "conf"
$nginxTemp = Join-Path $nginxRoot "temp"
New-Item -ItemType Directory -Force -Path (Join-Path $nginxTemp "client_body_temp") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $nginxTemp "proxy_temp") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $nginxTemp "fastcgi_temp") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $nginxTemp "uwsgi_temp") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $nginxTemp "scgi_temp") | Out-Null
New-Item -ItemType Directory -Force -Path $nginxLogs | Out-Null
New-Item -ItemType Directory -Force -Path $nginxConfDir | Out-Null

if ([string]::IsNullOrWhiteSpace($nginxExe) -or -not (Test-Path $nginxExe)) {
    $nginxDownloadRoot = Join-Path $repoRoot ".local\nginx-dist"
    New-Item -ItemType Directory -Force -Path $nginxDownloadRoot | Out-Null

    $zipPath = Join-Path $nginxDownloadRoot "nginx-$NginxVersion.zip"
    $extractRoot = Join-Path $nginxDownloadRoot "nginx-$NginxVersion"
    $nginxExe = Join-Path $extractRoot "nginx-$NginxVersion\nginx.exe"

    if (-not (Test-Path $nginxExe)) {
        if (-not (Test-Path $zipPath)) {
            $downloadUrl = "https://nginx.org/download/nginx-$NginxVersion.zip"
            Write-Host "Downloading NGINX $NginxVersion from $downloadUrl" -ForegroundColor Cyan
            Invoke-WebRequest -Uri $downloadUrl -OutFile $zipPath
        }

        Write-Host "Extracting NGINX to $extractRoot" -ForegroundColor Cyan
        if (Test-Path $extractRoot) {
            Remove-Item -Recurse -Force $extractRoot
        }
        Expand-Archive -Path $zipPath -DestinationPath $extractRoot
    }
}

if ([string]::IsNullOrWhiteSpace($nginxExe) -or -not (Test-Path $nginxExe)) {
    throw "NGINX executable not found after download. Provide -NginxPath or ensure network access to nginx.org."
}

$nginxConfPath = Join-Path $nginxConfDir "nginx.conf"
$nginxConfRelativePath = Join-Path "conf" "nginx.conf"
$upstreamServers = for ($i = 0; $i -lt $InstanceCount; $i++) {
    "    server 127.0.0.1:{0};" -f ($BasePort + $i)
}

@"
events {}

http {
  upstream fhir_upstream {
$($upstreamServers -join [Environment]::NewLine)
  }

  server {
    listen $NginxListenPort;
    location / {
      proxy_pass http://fhir_upstream;
      proxy_set_header Host `$http_host;
      proxy_set_header X-Forwarded-For `$proxy_add_x_forwarded_for;
      proxy_set_header X-Forwarded-Proto `$scheme;
      proxy_set_header X-Forwarded-Host `$http_host;
    }
  }
}
"@ | Set-Content -Path $nginxConfPath -Encoding UTF8

$testEnvUrl = "http://localhost:$NginxListenPort"
$env:TestEnvironmentUrl = $testEnvUrl
$env:TestEnvironmentUrl_R4 = $testEnvUrl
$env:TestEnvironmentUrl_Sql = $testEnvUrl
$env:TestEnvironmentUrl_R4_Sql = $testEnvUrl
$env:TestEnvironmentName = "local"
$env:TestProxyForwardedHost = "localhost"
$env:TestProxyForwardedPrefix = ""

if ($PersistTestEnvironment) {
    [Environment]::SetEnvironmentVariable("TestEnvironmentUrl", $testEnvUrl, "User")
    [Environment]::SetEnvironmentVariable("TestEnvironmentUrl_R4", $testEnvUrl, "User")
    [Environment]::SetEnvironmentVariable("TestEnvironmentUrl_Sql", $testEnvUrl, "User")
    [Environment]::SetEnvironmentVariable("TestEnvironmentUrl_R4_Sql", $testEnvUrl, "User")
    [Environment]::SetEnvironmentVariable("TestEnvironmentName", "local", "User")
    [Environment]::SetEnvironmentVariable("TestProxyForwardedHost", "localhost", "User")
    [Environment]::SetEnvironmentVariable("TestProxyForwardedPrefix", "", "User")
}

$logRoot = Join-Path $repoRoot ".local\logs"
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

Write-Host "Starting $InstanceCount FHIR Server instances (R4) on ports $BasePort..$($BasePort + $InstanceCount - 1)" -ForegroundColor Cyan

if (-not $SkipBuild) {
    Write-Host "Building project once to avoid file locks during parallel startup..." -ForegroundColor Cyan
    $buildArgs = @("build", $projectFullPath, "-f", $Framework)
    & dotnet @buildArgs
}

if (-not $SkipSchemaInitialization) {
    Write-Host "Ensuring SQL database exists and schema is initialized..." -ForegroundColor Cyan

    # Create DB if missing
    # Create DB if missing (no SqlConnectionStringBuilder — avoids Keyword not supported issues)
    try {
        # Hard-strip control chars + any wrapper token that might be hiding in the string
        $cs = $SqlConnectionString.Trim().Trim('"')
        $cs = [regex]::Replace($cs, '[\x00-\x1F]', '')
        $cs = $cs -replace '(?i)(^|;)\s*(ConnectionString|SqlServer:ConnectionString|DefaultConnection)\s*=\s*', '$1'

        # Extract Server + Database from the string without using SqlConnectionStringBuilder
        $server = ($cs -split ';' | Where-Object { $_ -match '^(?i)\s*(Server|Data Source)\s*=' } | Select-Object -First 1) -replace '^(?i)\s*(Server|Data Source)\s*=\s*', ''
        $dbName = ($cs -split ';' | Where-Object { $_ -match '^(?i)\s*(Database|Initial Catalog)\s*=' } | Select-Object -First 1) -replace '^(?i)\s*(Database|Initial Catalog)\s*=\s*', ''

        if ([string]::IsNullOrWhiteSpace($server)) { throw "Could not find Server/Data Source in connection string." }
        if ([string]::IsNullOrWhiteSpace($dbName)) { $dbName = $DatabaseName }
        if ([string]::IsNullOrWhiteSpace($dbName)) { throw "Could not determine database name." }

        $createDbSql = "IF DB_ID(N'$dbName') IS NULL CREATE DATABASE [$dbName];"

        # Use sqlcmd with integrated auth (-E). This avoids the builder entirely.
        $sqlcmd = Get-Command sqlcmd -ErrorAction SilentlyContinue
        if ($null -eq $sqlcmd) {
            throw "sqlcmd not found. Install 'SQL Server Command Line Utilities' or create the DB manually."
        }

        & $sqlcmd.Source -S $server -E -d master -Q $createDbSql | Out-Null
    }
    catch {
        Write-Warning "Failed to create database automatically. Ensure the database exists: $($_.Exception.Message)"
    }


    $schemaManagerProject = Join-Path $repoRoot "tools\DataStore\Microsoft.Health.Fhir.SchemaManager.Console"
    $schemaArgs = @(
        "run",
        "--project", $schemaManagerProject,
        "--framework", $Framework,
        "--",
        "apply",
        "--connection-string", $SqlConnectionString,
        "--latest"
    )

    if ($NoLaunchProfile) {
        $schemaArgs = @("run", "--no-launch-profile") + $schemaArgs[1..($schemaArgs.Length - 1)]
    }

    & dotnet @schemaArgs
}

$processes = @()
for ($i = 0; $i -lt $InstanceCount; $i++) {
    $port = $BasePort + $i
    $envVars = @{
        "ASPNETCORE_URLS" = "http://localhost:$port"
        "ASPNETCORE_ENVIRONMENT" = "Development"
        "DataStore" = "SqlServer"
        "SqlServer:ConnectionString" = $SqlConnectionString
        "SqlServer:AllowDatabaseCreation" = "true"
        "SqlServer:DeleteAllDataOnStartup" = "false"
        "ASPNETCORE_FORWARDEDHEADERS_ENABLED" = "true"
        "TaskHosting:Enabled" = "true"
        "TaskHosting:PollingFrequencyInSeconds" = "1"
        "TaskHosting:MaxRunningTaskCount" = "4"
        "FhirServer:CoreFeatures:SearchParameterCacheRefreshIntervalSeconds" = "1"
        "FhirServer:CoreFeatures:SearchParameterCacheRefreshMaxInitialDelaySeconds" = "0"
        "FhirServer:Operations:Reindex:Enabled" = "true"
        "FhirServer:Operations:Export:Enabled" = "true"
        "FhirServer:Operations:ConvertData:Enabled" = "true"
        "FhirServer:Operations:Import:Enabled" = "true"
        "Logging:LogLevel:Default" = "Debug"
        "Logging:LogLevel:Microsoft.Health" = "Debug"
        "Logging:LogLevel:Microsoft" = "Debug"
        "Logging:LogLevel:System" = "Debug"
    }

    if ($DisableSecurity) {
        $envVars["FhirServer:Security:Enabled"] = "false"
        $envVars["FhirServer:Security:Authorization:Enabled"] = "false"
    }

    $runArgs = @("run", "--no-build", "--no-restore", "--project", $projectFullPath, "--framework", $Framework)
    if ($NoLaunchProfile) {
        $runArgs += "--no-launch-profile"
    }

    if ($RedirectOutput) {
        $stdoutLog = Join-Path $logRoot "fhir-r4-$port.out.log"
        $stderrLog = Join-Path $logRoot "fhir-r4-$port.err.log"
        $process = Start-Process -FilePath "dotnet" -ArgumentList $runArgs -WorkingDirectory $repoRoot -Environment $envVars -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog -PassThru
    }
    else {
        $process = Start-Process -FilePath "dotnet" -ArgumentList $runArgs -WorkingDirectory $repoRoot -Environment $envVars -PassThru
    }

    $processes += $process
}

Write-Host "Starting NGINX on port $NginxListenPort" -ForegroundColor Cyan
try {
    $nginxPidPath = Join-Path $nginxLogs "nginx.pid"
    if (Test-Path $nginxPidPath) {
        Start-Process -FilePath $nginxExe -ArgumentList @("-p", $nginxRoot, "-c", $nginxConfRelativePath, "-s", "stop") -NoNewWindow -Wait -ErrorAction SilentlyContinue | Out-Null
    }
}
catch { }

Start-Process -FilePath $nginxExe -ArgumentList @("-p", $nginxRoot, "-c", $nginxConfRelativePath) -NoNewWindow | Out-Null

Write-Host "NGINX proxy: http://localhost:$NginxListenPort" -ForegroundColor Green
Write-Host "FHIR instances: " -NoNewline -ForegroundColor Green
$instanceUrls = for ($i = 0; $i -lt $InstanceCount; $i++) {
    "http://localhost:{0}" -f ($BasePort + $i)
}
Write-Host ($instanceUrls -join ", ") -ForegroundColor Green
Write-Host "E2E Test URL set: $env:TestEnvironmentUrl_R4_Sql" -ForegroundColor Green
if ($RedirectOutput) {
    Write-Host "Logs: $logRoot" -ForegroundColor Yellow
}
Write-Host "Press Ctrl+C to stop this script. You can stop NGINX with: `"$nginxExe -p $nginxRoot -s stop`"" -ForegroundColor Yellow

Wait-Process -Id ($processes | Select-Object -ExpandProperty Id)
