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

    [switch]$DisableSecurity = $true
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

# ---------------------------
# Helper: Write NGINX config with given upstream ports
# ---------------------------
function Write-NginxConfig {
    param(
        [Parameter(Mandatory=$true)][int[]]$Ports,
        [Parameter(Mandatory=$true)][int]$ListenPort,
        [Parameter(Mandatory=$true)][string]$ConfPath
    )

    $upstreamServers = foreach ($p in $Ports) {
        "    server 127.0.0.1:$p;"
    }

@"
events {}

http {
  upstream fhir_upstream {
$($upstreamServers -join [Environment]::NewLine)
  }

  server {
    listen $ListenPort;
    location / {
      proxy_pass http://fhir_upstream;
      proxy_set_header Host `$http_host;
      proxy_set_header X-Forwarded-For `$proxy_add_x_forwarded_for;
      proxy_set_header X-Forwarded-Proto `$scheme;
      proxy_set_header X-Forwarded-Host `$http_host;
    }
  }
}
"@ | Set-Content -Path $ConfPath -Encoding UTF8
}

# ---------------------------
# Helper: Start a single FHIR instance on a given port
# ---------------------------
function Start-FhirInstance {
    param(
        [Parameter(Mandatory=$true)][int]$Port
    )

    $envVars = @{
        "ASPNETCORE_URLS" = "http://localhost:$Port"
        "ASPNETCORE_ENVIRONMENT" = "Development"
        "DataStore" = "SqlServer"
        "SqlServer:ConnectionString" = $script:SqlConnectionString
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

    if ($script:DisableSecurity) {
        $envVars["FhirServer:Security:Enabled"] = "false"
        $envVars["FhirServer:Security:Authorization:Enabled"] = "false"
    }

    $runArgs = @("run", "--no-build", "--no-restore", "--project", $script:projectFullPath, "--framework", $script:Framework)
    if ($script:NoLaunchProfile) {
        $runArgs += "--no-launch-profile"
    }

    if ($script:RedirectOutput) {
        $stdoutLog = Join-Path $script:logRoot "fhir-r4-$Port.out.log"
        $stderrLog = Join-Path $script:logRoot "fhir-r4-$Port.err.log"
        $proc = Start-Process -FilePath "dotnet" -ArgumentList $runArgs -WorkingDirectory $script:repoRoot -Environment $envVars -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog -PassThru
    }
    else {
        $proc = Start-Process -FilePath "dotnet" -ArgumentList $runArgs -WorkingDirectory $script:repoRoot -Environment $envVars -PassThru
    }

    return [PSCustomObject]@{ Port = $Port; Process = $proc }
}

# ---------------------------
# Helper: Reload NGINX config (graceful, zero-downtime)
# ---------------------------
function Reload-Nginx {
    & $script:nginxExe -p $script:nginxRoot -c $script:nginxConfRelativePath -s reload 2>&1 | Out-Null
}


$SqlConnectionString = Normalize-SqlConnectionString -ConnectionString $SqlConnectionString -DatabaseNameFallback $DatabaseName

# ---------------------------
# Paths / validation
# ---------------------------
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")

# Support both absolute and relative project paths
if ([System.IO.Path]::IsPathRooted($ProjectPath)) {
    $projectFullPath = $ProjectPath
} else {
    $projectFullPath = Join-Path $repoRoot $ProjectPath
}

Write-Host "Using project path: $projectFullPath" -ForegroundColor Cyan

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

$initialPorts = @()
for ($i = 0; $i -lt $InstanceCount; $i++) {
    $initialPorts += ($BasePort + $i)
}
Write-NginxConfig -Ports $initialPorts -ListenPort $NginxListenPort -ConfPath $nginxConfPath

$testEnvUrl = "http://localhost:$NginxListenPort"

# Set process-scoped env vars
$env:TestEnvironmentUrl = $testEnvUrl
$env:TestEnvironmentUrl_R4 = $testEnvUrl
$env:TestEnvironmentUrl_Sql = $testEnvUrl
$env:TestEnvironmentUrl_R4_Sql = $testEnvUrl
$env:TestEnvironmentName = "local"
$env:TestProxyForwardedHost = "localhost"
$env:TestProxyForwardedPrefix = ""

# Persist to User scope so other terminals can run tests against this cluster
Write-Host "Persisting test environment variables to User scope..." -ForegroundColor Cyan
[Environment]::SetEnvironmentVariable("TestEnvironmentUrl", $testEnvUrl, "User")
[Environment]::SetEnvironmentVariable("TestEnvironmentUrl_R4", $testEnvUrl, "User")
[Environment]::SetEnvironmentVariable("TestEnvironmentUrl_Sql", $testEnvUrl, "User")
[Environment]::SetEnvironmentVariable("TestEnvironmentUrl_R4_Sql", $testEnvUrl, "User")
[Environment]::SetEnvironmentVariable("TestEnvironmentName", "local", "User")
[Environment]::SetEnvironmentVariable("TestProxyForwardedHost", "localhost", "User")
[Environment]::SetEnvironmentVariable("TestProxyForwardedPrefix", "", "User")
Write-Host "  Other terminals can now run tests against $testEnvUrl" -ForegroundColor Green

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

$script:instances = [System.Collections.Generic.List[PSCustomObject]]::new()
for ($i = 0; $i -lt $InstanceCount; $i++) {
    $port = $BasePort + $i
    $inst = Start-FhirInstance -Port $port
    $script:instances.Add($inst)
    Write-Host "  Started instance on port $port (PID $($inst.Process.Id))" -ForegroundColor DarkCyan
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
$instanceUrls = $script:instances | ForEach-Object { "http://localhost:$($_.Port)" }
Write-Host ($instanceUrls -join ", ") -ForegroundColor Green
Write-Host "E2E Test URL set: $env:TestEnvironmentUrl_R4_Sql" -ForegroundColor Green
if ($RedirectOutput) {
    Write-Host "Logs: $logRoot" -ForegroundColor Yellow
}
Write-Host ""
Write-Host "Interactive scaling mode. Commands:" -ForegroundColor Cyan
Write-Host "  +N      Scale up by N instances (default 1)" -ForegroundColor Cyan
Write-Host "  -N      Scale down by N instances (default 1, min 1 remaining)" -ForegroundColor Cyan
Write-Host "  status  Show all tracked instances" -ForegroundColor Cyan
Write-Host "  prune   Remove dead instances from NGINX upstream" -ForegroundColor Cyan
Write-Host "  help    Show this help" -ForegroundColor Cyan
Write-Host "  quit    Stop all instances and exit" -ForegroundColor Cyan
Write-Host ""

# ---------------------------
# Interactive command loop
# ---------------------------
try {
    while ($true) {
        # Health check: warn about dead instances
        foreach ($inst in $script:instances) {
            if ($inst.Process.HasExited) {
                Write-Host "WARNING: Instance on port $($inst.Port) (PID $($inst.Process.Id)) has exited." -ForegroundColor Red
            }
        }

        $liveCount = ($script:instances | Where-Object { -not $_.Process.HasExited } | Measure-Object).Count
        $totalCount = $script:instances.Count
        $promptLabel = if ($liveCount -eq $totalCount) { "$totalCount instances" } else { "$liveCount/$totalCount alive" }
        $userInput = Read-Host "fhir-runner [$promptLabel]"

        if ([string]::IsNullOrWhiteSpace($userInput)) { continue }

        $cmd = $userInput.Trim()

        # --- Scale UP: + or +N ---
        if ($cmd -match '^\+\s*(\d*)$') {
            $n = if ($Matches[1] -ne '') { [int]$Matches[1] } else { 1 }
            if ($n -lt 1) { Write-Host "Nothing to add." -ForegroundColor Yellow; continue }

            $maxPort = ($script:instances | ForEach-Object { $_.Port } | Measure-Object -Maximum).Maximum
            $newPorts = @()
            for ($j = 1; $j -le $n; $j++) {
                $newPort = $maxPort + $j
                $inst = Start-FhirInstance -Port $newPort
                $script:instances.Add($inst)
                $newPorts += $newPort
                Write-Host "  Started instance on port $newPort (PID $($inst.Process.Id))" -ForegroundColor Green
            }

            $allPorts = @($script:instances | Where-Object { -not $_.Process.HasExited } | ForEach-Object { $_.Port })
            Write-NginxConfig -Ports $allPorts -ListenPort $NginxListenPort -ConfPath $nginxConfPath
            Reload-Nginx
            Write-Host "NGINX reloaded. Now $($script:instances.Count) instances ($($allPorts.Count) alive)." -ForegroundColor Green
        }
        # --- Scale DOWN: - or -N ---
        elseif ($cmd -match '^-\s*(\d*)$') {
            $n = if ($Matches[1] -ne '') { [int]$Matches[1] } else { 1 }
            $alive = @($script:instances | Where-Object { -not $_.Process.HasExited })

            if ($alive.Count -le 1) {
                Write-Host "Cannot scale below 1 running instance." -ForegroundColor Red
                continue
            }

            $toRemove = [Math]::Min($n, $alive.Count - 1)
            # Remove from the end (highest ports)
            $removing = $script:instances | Sort-Object Port -Descending | Select-Object -First $toRemove

            foreach ($inst in $removing) {
                Write-Host "  Stopping instance on port $($inst.Port) (PID $($inst.Process.Id))..." -ForegroundColor Yellow
                try { Stop-Process -Id $inst.Process.Id -Force -ErrorAction SilentlyContinue } catch { }
                $script:instances.Remove($inst) | Out-Null
            }

            $allPorts = @($script:instances | Where-Object { -not $_.Process.HasExited } | ForEach-Object { $_.Port })
            if ($allPorts.Count -gt 0) {
                Write-NginxConfig -Ports $allPorts -ListenPort $NginxListenPort -ConfPath $nginxConfPath
                Reload-Nginx
            }
            Write-Host "NGINX reloaded. Removed $toRemove instance(s). $($script:instances.Count) remaining." -ForegroundColor Green
        }
        # --- Status ---
        elseif ($cmd -eq 'status') {
            Write-Host ("{0,-8} {1,-10} {2}" -f "Port", "PID", "Status") -ForegroundColor Cyan
            Write-Host ("{0,-8} {1,-10} {2}" -f "----", "---", "------") -ForegroundColor Cyan
            foreach ($inst in ($script:instances | Sort-Object Port)) {
                $status = if ($inst.Process.HasExited) { "Exited" } else { "Running" }
                $color = if ($inst.Process.HasExited) { "Red" } else { "Green" }
                Write-Host ("{0,-8} {1,-10} {2}" -f $inst.Port, $inst.Process.Id, $status) -ForegroundColor $color
            }
            $alive = ($script:instances | Where-Object { -not $_.Process.HasExited } | Measure-Object).Count
            Write-Host "Total: $($script:instances.Count) tracked, $alive alive." -ForegroundColor Cyan
        }
        # --- Prune dead instances ---
        elseif ($cmd -eq 'prune') {
            $dead = @($script:instances | Where-Object { $_.Process.HasExited })
            if ($dead.Count -eq 0) {
                Write-Host "No dead instances to prune." -ForegroundColor Green
                continue
            }

            foreach ($inst in $dead) {
                Write-Host "  Removing dead instance on port $($inst.Port) (PID $($inst.Process.Id))" -ForegroundColor Yellow
                $script:instances.Remove($inst) | Out-Null
            }

            $allPorts = @($script:instances | Where-Object { -not $_.Process.HasExited } | ForEach-Object { $_.Port })
            if ($allPorts.Count -gt 0) {
                Write-NginxConfig -Ports $allPorts -ListenPort $NginxListenPort -ConfPath $nginxConfPath
                Reload-Nginx
                Write-Host "NGINX reloaded. Pruned $($dead.Count) dead instance(s). $($script:instances.Count) remaining." -ForegroundColor Green
            }
            else {
                Write-Host "WARNING: No alive instances remain after prune." -ForegroundColor Red
            }
        }
        # --- Help ---
        elseif ($cmd -in @('help', '?')) {
            Write-Host "Commands:" -ForegroundColor Cyan
            Write-Host "  +N      Scale up by N instances (default 1)" -ForegroundColor Cyan
            Write-Host "  -N      Scale down by N instances (default 1, min 1 remaining)" -ForegroundColor Cyan
            Write-Host "  status  Show all tracked instances" -ForegroundColor Cyan
            Write-Host "  prune   Remove dead instances from NGINX upstream" -ForegroundColor Cyan
            Write-Host "  quit    Stop all instances and exit" -ForegroundColor Cyan
        }
        # --- Quit ---
        elseif ($cmd -in @('quit', 'q', 'exit')) {
            break
        }
        else {
            Write-Host "Unknown command: '$cmd'. Type 'help' for available commands." -ForegroundColor Yellow
        }
    }
}
finally {
    Write-Host ""
    Write-Host "Shutting down..." -ForegroundColor Cyan

    foreach ($inst in $script:instances) {
        if (-not $inst.Process.HasExited) {
            Write-Host "  Stopping instance on port $($inst.Port) (PID $($inst.Process.Id))..." -ForegroundColor Yellow
            try { Stop-Process -Id $inst.Process.Id -Force -ErrorAction SilentlyContinue } catch { }
        }
    }

    try {
        Write-Host "  Stopping NGINX..." -ForegroundColor Yellow
        & $nginxExe -p $nginxRoot -c $nginxConfRelativePath -s stop 2>&1 | Out-Null
    }
    catch { }

    # Clean up User-scoped environment variables
    Write-Host "  Removing test environment variables from User scope..." -ForegroundColor Yellow
    [Environment]::SetEnvironmentVariable("TestEnvironmentUrl", $null, "User")
    [Environment]::SetEnvironmentVariable("TestEnvironmentUrl_R4", $null, "User")
    [Environment]::SetEnvironmentVariable("TestEnvironmentUrl_Sql", $null, "User")
    [Environment]::SetEnvironmentVariable("TestEnvironmentUrl_R4_Sql", $null, "User")
    [Environment]::SetEnvironmentVariable("TestEnvironmentName", $null, "User")
    [Environment]::SetEnvironmentVariable("TestProxyForwardedHost", $null, "User")
    [Environment]::SetEnvironmentVariable("TestProxyForwardedPrefix", $null, "User")

    Write-Host "All instances and NGINX stopped." -ForegroundColor Green
}
