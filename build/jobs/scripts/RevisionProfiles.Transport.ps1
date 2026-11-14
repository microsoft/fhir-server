$script:AccessToken = $null
$script:ApiVersion = '2024-03-01'

function Get-ArmResponseHeader {
    param($Headers, [string]$Name)

    $values = @($Headers[$Name] | Where-Object { $null -ne $_ })
    if ($values.Count -eq 0) {
        return $null
    }

    if ($values.Count -ne 1 -or [string]::IsNullOrWhiteSpace([string]$values[0])) {
        throw "ARM operation returned an invalid $Name header."
    }

    return [string]$values[0]
}

function Get-ArmOperationDelay {
    param($Headers)

    $value = Get-ArmResponseHeader -Headers $Headers -Name 'Retry-After'
    if ($null -eq $value) {
        return $script:PollDelaySeconds
    }

    [int]$seconds = 0
    if (-not [int]::TryParse($value, [ref]$seconds) -or $seconds -lt 0) {
        throw 'ARM operation returned an invalid Retry-After header.'
    }

    return [Math]::Max($seconds, $script:PollDelaySeconds)
}

function Invoke-ArmStatusRequest {
    param([uri]$Uri, [TimeSpan]$Timeout)

    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [System.Net.Http.HttpClient]::new($handler)
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, $Uri)
    $credential = [System.Net.NetworkCredential]::new('', $script:AccessToken)
    $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $credential.Password)
    $cancellation = [System.Threading.CancellationTokenSource]::new($Timeout)
    $response = $null
    try {
        $response = $client.SendAsync($request, [System.Net.Http.HttpCompletionOption]::ResponseContentRead, $cancellation.Token).GetAwaiter().GetResult()
        $content = $response.Content.ReadAsStringAsync($cancellation.Token).GetAwaiter().GetResult()
        $headers = @{}
        foreach ($header in $response.Headers) {
            $headers[$header.Key] = @($header.Value)
        }

        return [pscustomobject]@{ StatusCode = [int]$response.StatusCode; Headers = $headers; Content = $content }
    }
    catch [System.OperationCanceledException] {
        throw 'ARM operation status request timed out.'
    }
    finally {
        if ($null -ne $response) {
            $response.Dispose()
        }

        $cancellation.Dispose()
        $request.Dispose()
        $client.Dispose()
    }
}

function Wait-ArmOperation {
    param($Response)

    $address = Get-ArmResponseHeader -Headers $Response.Headers -Name 'Azure-AsyncOperation'
    $isAsyncOperation = $null -ne $address
    if (-not $isAsyncOperation) {
        $address = Get-ArmResponseHeader -Headers $Response.Headers -Name 'Location'
    }

    if ($null -eq $address) {
        throw 'ARM accepted an update but the operation status header is missing.'
    }

    try {
        $uri = [uri]::new([uri]'https://management.azure.com/', $address)
    }
    catch [System.UriFormatException] {
        throw 'ARM returned an invalid operation URL.'
    }

    if ($uri.Scheme -cne 'https' -or $uri.Host -ine 'management.azure.com' -or $uri.Port -ne 443 -or
        -not [string]::IsNullOrEmpty($uri.UserInfo) -or -not [string]::IsNullOrEmpty($uri.Fragment)) {
        throw 'ARM returned an untrusted operation URL.'
    }

    $delay = Get-ArmOperationDelay -Headers $Response.Headers
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $limit = $script:PollAttempts * [Math]::Max(1, $script:PollDelaySeconds)
    for ($attempt = 0; $attempt -lt $script:PollAttempts; $attempt++) {
        if ($clock.Elapsed.TotalSeconds + $delay -gt $limit) {
            break
        }

        if ($delay -gt 0) {
            Start-Sleep -Seconds $delay
        }

        $remaining = $limit - $clock.Elapsed.TotalSeconds
        if ($remaining -le 0) {
            break
        }

        $poll = Invoke-ArmStatusRequest -Uri $uri -Timeout ([TimeSpan]::FromSeconds([Math]::Min(30, $remaining)))
        if ($clock.Elapsed.TotalSeconds -ge $limit) {
            break
        }

        if ($poll.StatusCode -notin @(200, 202, 204)) {
            throw "ARM operation status request returned unsupported HTTP $($poll.StatusCode)."
        }

        if (-not $isAsyncOperation -and $poll.StatusCode -eq 202) {
            $delay = Get-ArmOperationDelay -Headers $poll.Headers
            continue
        }

        $body = $null
        if (-not [string]::IsNullOrWhiteSpace([string]$poll.Content)) {
            try {
                $body = ConvertFrom-Json -InputObject ([string]$poll.Content) -AsHashtable -Depth 100 -ErrorAction Stop
            }
            catch [System.ArgumentException] {
                throw 'ARM operation returned invalid JSON.'
            }

            if ($body -isnot [System.Collections.IDictionary]) {
                throw 'ARM operation returned an invalid status document.'
            }
        }

        $status = Get-StringField $body 'status'
        if ([string]::IsNullOrEmpty($status) -and -not $isAsyncOperation) {
            $status = Get-StringField (Get-Field $body 'properties') 'provisioningState'
        }

        if ($status -ieq 'Succeeded') {
            return
        }

        if ($status -iin @('Failed', 'Canceled', 'Cancelled')) {
            $code = Get-StringField (Get-Field $body 'error') 'code'
            if ($code -cmatch '^[A-Za-z0-9._-]{1,128}$') {
                throw "ARM operation $status with code '$code'."
            }

            throw "ARM operation $status."
        }

        if ([string]::IsNullOrEmpty($status)) {
            if ($poll.StatusCode -in @(200, 204) -and -not $isAsyncOperation) {
                return
            }

            if ($poll.StatusCode -ne 202) {
                throw 'ARM operation status is missing.'
            }
        }

        $delay = Get-ArmOperationDelay -Headers $poll.Headers
    }

    throw 'ARM operation was not completed within the polling limit; the accepted write was not repeated.'
}

function Invoke-ArmRequest {
    param([Parameter(Mandatory)][string]$Method, [Parameter(Mandatory)][string]$Path, [object]$Body)

    if ($null -eq $script:AccessToken) {
        throw 'The revision transport requires an explicit ARM access token.'
    }

    $query = if ($Path.Contains('?')) { '' } else { "?api-version=$($script:ApiVersion)" }
    $parameters = @{
        Method = $Method
        Uri = "https://management.azure.com$Path$query"
        Authentication = 'Bearer'
        Token = $script:AccessToken
        ErrorAction = 'Stop'
    }

    if ($null -ne $Body) {
        $parameters['Body'] = ConvertTo-Json -InputObject $Body -Depth 50 -Compress
        $parameters['ContentType'] = 'application/json'
    }

    try {
        $response = Invoke-WebRequest @parameters
    }
    catch [Microsoft.PowerShell.Commands.HttpResponseException] {
        $isRevisionRead = $Method -ceq 'GET' -and $Path -cmatch '/revisions/[^/?]+$'
        if ($isRevisionRead -and $_.Exception.Response.StatusCode -eq [System.Net.HttpStatusCode]::NotFound) {
            return $null
        }

        throw "ARM $Method $Path failed with HTTP $([int]$_.Exception.Response.StatusCode)."
    }

    if ($response.StatusCode -eq 202 -and $Method -cin @('PATCH', 'POST')) {
        Wait-ArmOperation -Response $response
    }

    if ([string]::IsNullOrWhiteSpace([string]$response.Content)) {
        return $null
    }

    return ConvertFrom-Json -InputObject ([string]$response.Content) -AsHashtable -Depth 100
}

function Invoke-RevisionHealthProbe {
    param([Parameter(Mandatory)][string]$Url)

    try {
        $response = Invoke-WebRequest -Uri "$Url/health/check" -Method 'GET' -SkipHttpErrorCheck -TimeoutSec 30 -ErrorAction Stop
        return $response.StatusCode -eq 200
    }
    catch [System.Net.Http.HttpRequestException] {
        return $false
    }
    catch [System.Threading.Tasks.TaskCanceledException] {
        return $false
    }
    catch [System.TimeoutException] {
        return $false
    }
}
