$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot 'RevisionProfiles.psm1') -Force
$module = Get-Module RevisionProfiles
$token = ConvertTo-SecureString 'fixture-token' -AsPlainText -Force
$script:Failures = [System.Collections.Generic.List[string]]::new()

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -cne $Actual) {
        throw "ASSERT FAILED: $Message. Expected '$Expected' but got '$Actual'."
    }
}

function Assert-Throws {
    param([scriptblock]$Action, [string]$Pattern, [string]$Message)
    $failure = $null
    try {
        & $Action | Out-Null
    }
    catch {
        $failure = $_.Exception.Message
    }

    if ($null -eq $failure -or $failure -notmatch $Pattern) {
        throw "ASSERT FAILED: $Message. Expected '$Pattern', got '$failure'."
    }

    if ($failure -match 'fixture-secret') {
        throw "ASSERT FAILED: $Message exposed the operation error body."
    }
}

function Invoke-Case {
    param([string]$Name, [scriptblock]$Body)
    try {
        & $Body
        Write-Host "PASS $Name"
    }
    catch {
        $script:Failures.Add($Name)
        Write-Host "FAIL $Name :: $($_.Exception.Message)"
    }
}

function Start-StatusServer {
    param([int]$DelaySeconds, [bool]$Redirect = $false)

    $job = Start-Job -ScriptBlock {
        param($DelaySeconds, $Redirect)
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
        $listener.Start()
        try {
            Write-Output $listener.LocalEndpoint.Port
            $socket = $listener.AcceptTcpClient()
            try {
                $stream = $socket.GetStream()
                $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::ASCII, $false, 1024, $true)
                $authorized = $false
                while (($line = $reader.ReadLine()) -ne '') {
                    if ($null -eq $line) { throw 'The request ended before its headers.' }
                    if ($line -ceq 'Authorization: Bearer fixture-token') { $authorized = $true }
                }

                if (-not $authorized) { throw 'The request did not contain the fixture bearer token.' }
                if ($Redirect) {
                    $redirectHeaders = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 302 Found`r`nLocation: http://127.0.0.1:1/other`r`nContent-Length: 0`r`nConnection: close`r`n`r`n")
                    $stream.Write($redirectHeaders, 0, $redirectHeaders.Length)
                    return
                }

                $body = [Text.Encoding]::UTF8.GetBytes('{"status":"Succeeded"}')
                $headers = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 200 OK`r`nContent-Type: application/json`r`nRetry-After: 3`r`nContent-Length: $($body.Length)`r`nConnection: close`r`n`r`n")
                $stream.Write($headers, 0, $headers.Length)
                $stream.Flush()
                Start-Sleep -Seconds $DelaySeconds
                $stream.Write($body, 0, $body.Length)
            }
            finally {
                $socket.Dispose()
            }
        }
        finally {
            $listener.Stop()
        }
    } -ArgumentList $DelaySeconds, $Redirect

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $port = Receive-Job -Job $job
        if ($port) {
            return @{ Job = $job; Uri = "http://127.0.0.1:$port/status" }
        }

        if ($job.State -notin @('Running', 'NotStarted')) {
            break
        }

        Start-Sleep -Milliseconds 50
    }

    Stop-Job -Job $job
    Remove-Job -Job $job
    throw 'The local status server did not start.'
}

& $module {
    param($AccessToken)
    $script:AccessToken = $AccessToken
    $script:PollAttempts = 3
    $script:PollDelaySeconds = 0
    $script:FixtureAppId = '/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/fixture/providers/Microsoft.App/containerApps/fixture'
    $script:FixtureOperation = 'https://management.azure.com/subscriptions/00000000-0000-0000-0000-000000000000/providers/Microsoft.App/locations/westus2/containerappOperationResults/fixture?api-version=2024-03-01'
    $script:RealArmStatusRequest = (Get-Item Function:\Invoke-ArmStatusRequest).ScriptBlock

    function script:Reset-FixtureTransport {
        param([string]$Scenario)
        $script:FixtureTransport = @{
            Scenario = $Scenario
            Busy = $false
            Attempts = 0
            Writes = 0
            Polls = 0
            Sleeps = [System.Collections.Generic.List[int]]::new()
        }
    }

    function script:Start-Sleep {
        param([int]$Seconds)
        $script:FixtureTransport.Sleeps.Add($Seconds)
    }

    function script:New-FixtureHeaders {
        param([hashtable]$Values)
        $headers = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.IEnumerable[string]]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($name in $Values.Keys) {
            $headers[$name] = [string[]]@($Values[$name])
        }

        return , $headers
    }

    function script:Invoke-WebRequest {
        [CmdletBinding()]
        param($Method, $Uri, $Authentication, $Token, $Body, $ContentType, $MaximumRedirection, $TimeoutSec)

        if ($Method -cin @('PATCH', 'POST')) {
            $script:FixtureTransport.Attempts++
            if ($script:FixtureTransport.Busy) {
                throw 'HTTP 409 ContainerAppOperationInProgress'
            }

            $script:FixtureTransport.Writes++
            if ($script:FixtureTransport.Scenario -ceq 'Synchronous') {
                return [pscustomobject]@{ StatusCode = 200; Headers = (New-FixtureHeaders @{}); Content = '{"result":"synchronous"}' }
            }

            $script:FixtureTransport.Busy = $true
            $headers = switch ($script:FixtureTransport.Scenario) {
                'MissingHeader' { @{} }
                'UnsafeHeader' { @{ 'Azure-AsyncOperation' = 'https://management.azure.com.attacker.invalid/status' } }
                'Location' { @{ Location = $script:FixtureOperation } }
                'LocationPendingStatus' { @{ Location = $script:FixtureOperation } }
                'LocationPendingFailure' { @{ Location = $script:FixtureOperation } }
                'LocationFailed' { @{ Location = $script:FixtureOperation } }
                'BothHeaders' { @{ 'Azure-AsyncOperation' = $script:FixtureOperation; Location = 'https://management.azure.com.attacker.invalid/status' } }
                'RetryAfter' { @{ 'Azure-AsyncOperation' = $script:FixtureOperation; 'Retry-After' = '2' } }
                default { @{ 'Azure-AsyncOperation' = $script:FixtureOperation } }
            }
            return [pscustomobject]@{ StatusCode = 202; Headers = (New-FixtureHeaders $headers); Content = '' }
        }

        if ($Method -ceq 'GET' -and $script:FixtureTransport.Scenario -ceq 'SlowBody') {
            return Microsoft.PowerShell.Utility\Invoke-WebRequest -Method GET -Uri $Uri -TimeoutSec $TimeoutSec -ErrorAction Stop
        }

        if ($Method -cne 'GET' -or $Uri -cne $script:FixtureOperation) {
            throw "Unexpected fixture request $Method $Uri."
        }

        $script:FixtureTransport.Polls++
        switch ($script:FixtureTransport.Scenario) {
            'Failed' { return [pscustomobject]@{ StatusCode = 200; Headers = (New-FixtureHeaders @{}); Content = '{"status":"Failed","error":{"code":"FixtureFailure","message":"fixture-secret"}}' } }
            'LocationFailed' { return [pscustomobject]@{ StatusCode = 200; Headers = (New-FixtureHeaders @{}); Content = '{"properties":{"provisioningState":"Failed"}}' } }
            'Canceled' { return [pscustomobject]@{ StatusCode = 200; Headers = (New-FixtureHeaders @{}); Content = '{"status":"Canceled","error":{"code":"FixtureCanceled","message":"fixture-secret"}}' } }
            'Timeout' { return [pscustomobject]@{ StatusCode = 200; Headers = (New-FixtureHeaders @{}); Content = '{"status":"InProgress"}' } }
            'MalformedStatus' { return [pscustomobject]@{ StatusCode = 200; Headers = (New-FixtureHeaders @{}); Content = 'not-json' } }
            'MissingStatus' { return [pscustomobject]@{ StatusCode = 200; Headers = (New-FixtureHeaders @{}); Content = '{}' } }
            'UnexpectedHttpStatus' { return [pscustomobject]@{ StatusCode = 201; Headers = (New-FixtureHeaders @{}); Content = '{"status":"Succeeded"}' } }
            'Location' {
                if ($script:FixtureTransport.Polls % 2 -eq 1) {
                    return [pscustomobject]@{ StatusCode = 202; Headers = (New-FixtureHeaders @{}); Content = '' }
                }

                $script:FixtureTransport.Busy = $false
                return [pscustomobject]@{ StatusCode = 200; Headers = (New-FixtureHeaders @{}); Content = '' }
            }
            'LocationPendingStatus' {
                if ($script:FixtureTransport.Polls % 2 -eq 1) {
                    return [pscustomobject]@{ StatusCode = 202; Headers = (New-FixtureHeaders @{}); Content = '{"properties":{"provisioningState":"Succeeded"}}' }
                }

                $script:FixtureTransport.Busy = $false
                return [pscustomobject]@{ StatusCode = 200; Headers = (New-FixtureHeaders @{}); Content = '' }
            }
            'LocationPendingFailure' {
                if ($script:FixtureTransport.Polls % 2 -eq 1) {
                    return [pscustomobject]@{ StatusCode = 202; Headers = (New-FixtureHeaders @{}); Content = '{"properties":{"provisioningState":"Failed"}}' }
                }

                $script:FixtureTransport.Busy = $false
                return [pscustomobject]@{ StatusCode = 200; Headers = (New-FixtureHeaders @{}); Content = '' }
            }
            default {
                if ($script:FixtureTransport.Polls % 2 -eq 1) {
                    $headers = if ($script:FixtureTransport.Scenario -ceq 'RetryAfter') { @{ 'Retry-After' = '1' } } else { @{} }
                    return [pscustomobject]@{ StatusCode = 200; Headers = (New-FixtureHeaders $headers); Content = '{"status":"InProgress"}' }
                }

                $script:FixtureTransport.Busy = $false
                return [pscustomobject]@{ StatusCode = 200; Headers = (New-FixtureHeaders @{}); Content = '{"status":"Succeeded"}' }
            }
        }
    }

    function script:Invoke-ArmStatusRequest {
        param([uri]$Uri, [TimeSpan]$Timeout)

        if ($script:FixtureTransport.Scenario -cin @('SlowBody', 'LiveStatus')) {
            return & $script:RealArmStatusRequest -Uri ([uri]$script:FixtureServer) -Timeout $Timeout
        }

        return Invoke-WebRequest -Method 'GET' -Uri $Uri.AbsoluteUri -Authentication 'Bearer' -Token $script:AccessToken `
            -MaximumRedirection 0 -TimeoutSec ([Math]::Max(1, [int][Math]::Ceiling($Timeout.TotalSeconds))) -ErrorAction Stop
    }
} $token

try {
    Invoke-Case 'accepted updates complete before another update is submitted' {
        $state = & $module {
            Reset-FixtureTransport 'Success'
            Invoke-ArmRequest -Method 'PATCH' -Path $script:FixtureAppId -Body @{ properties = @{ configuration = @{} } } | Out-Null
            Invoke-ArmRequest -Method 'PATCH' -Path $script:FixtureAppId -Body @{ properties = @{ template = @{} } } | Out-Null
            $script:FixtureTransport
        }
        Assert-Equal 2 $state.Writes 'each accepted update is submitted once'
        Assert-Equal 4 $state.Polls 'both accepted operations reach their terminal state'
    }

    Invoke-Case 'a Location operation completes before the next update' {
        $state = & $module {
            Reset-FixtureTransport 'Location'
            Invoke-ArmRequest -Method 'PATCH' -Path $script:FixtureAppId -Body @{ properties = @{ configuration = @{} } } | Out-Null
            Invoke-ArmRequest -Method 'PATCH' -Path $script:FixtureAppId -Body @{ properties = @{ template = @{} } } | Out-Null
            $script:FixtureTransport
        }
        Assert-Equal 2 $state.Writes 'each accepted update is submitted once'
        Assert-Equal 4 $state.Polls 'both Location operations reach their terminal state'
    }

    Invoke-Case 'a Location 202 response remains pending even with a succeeded resource state' {
        $state = & $module {
            Reset-FixtureTransport 'LocationPendingStatus'
            Invoke-ArmRequest -Method 'PATCH' -Path $script:FixtureAppId -Body @{ properties = @{ configuration = @{} } } | Out-Null
            Invoke-ArmRequest -Method 'PATCH' -Path $script:FixtureAppId -Body @{ properties = @{ template = @{} } } | Out-Null
            $script:FixtureTransport
        }
        Assert-Equal 2 $state.Writes 'each accepted update is submitted once'
        Assert-Equal 4 $state.Polls 'both Location operations reach their terminal HTTP response'
    }

    Invoke-Case 'a Location 202 response remains pending even with a failed resource state' {
        $state = & $module {
            Reset-FixtureTransport 'LocationPendingFailure'
            Invoke-ArmRequest -Method 'PATCH' -Path $script:FixtureAppId -Body @{ properties = @{ configuration = @{} } } | Out-Null
            Invoke-ArmRequest -Method 'PATCH' -Path $script:FixtureAppId -Body @{ properties = @{ template = @{} } } | Out-Null
            $script:FixtureTransport
        }
        Assert-Equal 2 $state.Writes 'each accepted update is submitted once'
        Assert-Equal 4 $state.Polls 'both Location operations reach their terminal HTTP response'
    }

    Invoke-Case 'an accepted revision deactivation completes before the next update' {
        $state = & $module {
            Reset-FixtureTransport 'Success'
            Invoke-ArmRequest -Method 'POST' -Path "$script:FixtureAppId/revisions/fixture--old/deactivate" -Body $null | Out-Null
            Invoke-ArmRequest -Method 'PATCH' -Path $script:FixtureAppId -Body @{ properties = @{ configuration = @{} } } | Out-Null
            $script:FixtureTransport
        }
        Assert-Equal 2 $state.Writes 'deactivation and update are each submitted once'
        Assert-Equal 4 $state.Polls 'both operations reach their terminal state'
    }

    Invoke-Case 'Azure-AsyncOperation takes precedence over Location' {
        $state = & $module {
            Reset-FixtureTransport 'BothHeaders'
            Invoke-ArmRequest -Method 'PATCH' -Path $script:FixtureAppId -Body @{ properties = @{ configuration = @{} } } | Out-Null
            $script:FixtureTransport
        }
        Assert-Equal 2 $state.Polls 'only the trusted Azure-AsyncOperation URL is polled'
    }

    Invoke-Case 'operation polling honors Retry-After headers' {
        $state = & $module {
            Reset-FixtureTransport 'RetryAfter'
            Invoke-ArmRequest -Method 'PATCH' -Path $script:FixtureAppId -Body @{ properties = @{ configuration = @{} } } | Out-Null
            $script:FixtureTransport
        }
        Assert-Equal '2,1' ($state.Sleeps -join ',') 'initial and subsequent server-requested delays are honored'
    }

    Invoke-Case 'the status transport sends the bearer token and reads response headers and JSON' {
        $server = Start-StatusServer 0
        try {
            $result = & $module {
                param($ServerUri)
                & $script:RealArmStatusRequest -Uri ([uri]$ServerUri) -Timeout ([TimeSpan]::FromSeconds(10))
            } $server.Uri
            Assert-Equal 200 $result.StatusCode 'the HTTP response status is retained'
            Assert-Equal '3' $result.Headers['Retry-After'][0] 'the Retry-After header is retained'
            Assert-Equal '{"status":"Succeeded"}' $result.Content 'the JSON response body is retained'
        }
        finally {
            Stop-Job -Job $server.Job
            Remove-Job -Job $server.Job
        }
    }

    Invoke-Case 'the status transport never follows a redirect' {
        $server = Start-StatusServer 0 $true
        try {
            $result = & $module {
                param($ServerUri)
                & $script:RealArmStatusRequest -Uri ([uri]$ServerUri) -Timeout ([TimeSpan]::FromSeconds(10))
            } $server.Uri
            Assert-Equal 302 $result.StatusCode 'the redirect response is returned without following it'
        }
        finally {
            Stop-Job -Job $server.Job
            Remove-Job -Job $server.Job
        }
    }

    Invoke-Case 'an accepted update completes through the real status transport' {
        $server = Start-StatusServer 0
        try {
            $state = & $module {
                param($ServerUri)
                Reset-FixtureTransport 'LiveStatus'
                $script:FixtureServer = $ServerUri
                Invoke-ArmRequest -Method 'PATCH' -Path $script:FixtureAppId -Body @{ properties = @{ configuration = @{} } } | Out-Null
                $script:FixtureTransport
            } $server.Uri
            Assert-Equal 1 $state.Writes 'the accepted update is submitted once'
        }
        finally {
            Stop-Job -Job $server.Job
            Remove-Job -Job $server.Job
        }
    }

    Invoke-Case 'a stalled status response cannot exceed the polling deadline' {
        $server = Start-StatusServer 4
        try {
            & $module {
                param($ServerUri)
                Reset-FixtureTransport 'SlowBody'
                $script:FixtureServer = $ServerUri
                $script:PollAttempts = 1
            } $server.Uri
            $clock = [Diagnostics.Stopwatch]::StartNew()
            Assert-Throws {
                & $module {
                    Invoke-ArmRequest -Method 'PATCH' -Path $script:FixtureAppId -Body @{ properties = @{ configuration = @{} } } | Out-Null
                    Invoke-ArmRequest -Method 'PATCH' -Path $script:FixtureAppId -Body @{ properties = @{ template = @{} } } | Out-Null
                }
            } 'timed out|timeout|polling limit' 'a stalled response must time out before another update'
            if ($clock.Elapsed.TotalSeconds -ge 3.5) {
                throw 'ASSERT FAILED: The polling deadline did not bound the response body read.'
            }
            $state = & $module { $script:FixtureTransport }
            Assert-Equal 1 $state.Writes 'the accepted write is not replayed'
            Assert-Equal 1 $state.Attempts 'a second update is not submitted'
        }
        finally {
            & $module { $script:PollAttempts = 3 }
            Stop-Job -Job $server.Job
            Remove-Job -Job $server.Job
        }
    }

    foreach ($scenario in @('Failed', 'LocationFailed', 'Canceled', 'Timeout', 'MissingHeader', 'UnsafeHeader', 'MalformedStatus', 'MissingStatus', 'UnexpectedHttpStatus')) {
        Invoke-Case "an accepted $scenario operation stops before another update" {
            & $module { param($Name) Reset-FixtureTransport $Name } $scenario
            $pattern = switch ($scenario) {
                'Failed' { 'Failed|FixtureFailure' }
                'LocationFailed' { 'Failed' }
                'Canceled' { 'Canceled|FixtureCanceled' }
                'Timeout' { 'not completed|timed out|timeout' }
                'MissingHeader' { 'missing.*(header|operation)|(header|operation).*(missing|required)' }
                'UnsafeHeader' { 'invalid|unsafe|unsupported|untrusted' }
                'MalformedStatus' { 'invalid|json' }
                'MissingStatus' { 'missing|status' }
                'UnexpectedHttpStatus' { 'unsupported.*201' }
            }
            Assert-Throws {
                & $module {
                    Invoke-ArmRequest -Method 'PATCH' -Path $script:FixtureAppId -Body @{ properties = @{ configuration = @{} } } | Out-Null
                    Invoke-ArmRequest -Method 'PATCH' -Path $script:FixtureAppId -Body @{ properties = @{ template = @{} } } | Out-Null
                }
            } $pattern "$scenario is surfaced without a second write"
            $state = & $module { $script:FixtureTransport }
            Assert-Equal 1 $state.Writes 'the accepted write is not replayed'
            Assert-Equal 1 $state.Attempts 'a second update is not submitted'
            if ($scenario -in @('MissingHeader', 'UnsafeHeader')) {
                Assert-Equal 0 $state.Polls 'an invalid operation URL is never requested'
            }
        }
    }

    Invoke-Case 'synchronous ARM updates retain their response body' {
        $result = & $module {
            Reset-FixtureTransport 'Synchronous'
            Invoke-ArmRequest -Method 'PATCH' -Path $script:FixtureAppId -Body @{ properties = @{ configuration = @{} } }
        }
        Assert-Equal 'synchronous' $result.result 'synchronous response stays available'
    }

    if ($script:Failures.Count -gt 0) {
        throw "$($script:Failures.Count) ARM transport case(s) failed: $($script:Failures -join '; ')"
    }

    Write-Host 'All ARM transport cases passed.'
}
finally {
    Remove-Module RevisionProfiles -ErrorAction SilentlyContinue
}
