<#
.SYNOPSIS
Defines the Invoke-WithRetry function for executing script blocks with retry logic using exponential backoff.

.DESCRIPTION
This script defines the Invoke-WithRetry function. Dot-source this file to make the function available in your session.

.EXAMPLE
. ./Invoke-WithRetry.ps1
Invoke-WithRetry -ScriptBlock { Get-AzCosmosDBAccount -Name "myaccount" -ResourceGroupName "myrg" -ErrorAction Stop }
#>

function Invoke-WithRetry {
    <#
    .SYNOPSIS
    Executes a script block with retry logic using exponential backoff.

    .DESCRIPTION
    Invokes the specified script block and retries on transient errors (resource not found, conflict, etc.)
    using exponential backoff delays between attempts.

    .PARAMETER ScriptBlock
    The script block to execute.

    .PARAMETER MaxRetries
    Maximum number of retry attempts. Default is 5.

    .PARAMETER InitialDelaySeconds
    Initial delay in seconds before the first retry. Subsequent delays double (exponential backoff).
    Default is 15 seconds. Sequence: 15s, 30s, 60s, 120s, 240s.

    .PARAMETER RetryableErrorPatterns
    Array of wildcard patterns to match against error messages to determine if retry should occur.
    Default patterns match common Azure transient errors: "not found", "NotFound", "Conflict".

    .PARAMETER OperationName
    Optional friendly name for the operation, used in log messages.

    .EXAMPLE
    Invoke-WithRetry -ScriptBlock { Get-AzCosmosDBAccount -Name "myaccount" -ResourceGroupName "myrg" -ErrorAction Stop }

    .EXAMPLE
    Invoke-WithRetry -ScriptBlock { New-AzCosmosDBSqlRoleAssignment ... } -MaxRetries 3 -OperationName "CosmosDB Role Assignment"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [ScriptBlock]$ScriptBlock,

        [Parameter(Mandatory = $false)]
        [int]$MaxRetries = 5,

        [Parameter(Mandatory = $false)]
        [int]$InitialDelaySeconds = 15,

        [Parameter(Mandatory = $false)]
        [string[]]$RetryableErrorPatterns = @("*was not found*", "*NotFound*", "*Conflict*", "*ResourceNotFound*"),

        [Parameter(Mandatory = $false)]
        [string]$OperationName = "Operation"
    )

    $attempt = 0
    $success = $false
    $result = $null

    do {
        $attempt++
        try {
            Write-Host "[$OperationName] Attempt $attempt of $MaxRetries..."
            $result = & $ScriptBlock
            $success = $true
            Write-Host "[$OperationName] Succeeded on attempt $attempt"
        }
        catch {
            $errorMessage = $_.Exception.Message
            $isRetryable = $false

            foreach ($pattern in $RetryableErrorPatterns) {
                if ($errorMessage -like $pattern) {
                    $isRetryable = $true
                    break
                }
            }

            if ($isRetryable -and $attempt -lt $MaxRetries) {
                # Calculate exponential backoff delay: InitialDelay * 2^(attempt-1)
                $delaySeconds = $InitialDelaySeconds * [Math]::Pow(2, $attempt - 1)
                Write-Warning "[$OperationName] Transient error detected (attempt $attempt of $MaxRetries). Retrying in $delaySeconds seconds..."
                Write-Warning "[$OperationName] Error: $errorMessage"
                Start-Sleep -Seconds $delaySeconds
            }
            else {
                Write-Error "[$OperationName] Failed after $attempt attempt(s). Error: $errorMessage"
                throw
            }
        }
    } while (-not $success -and $attempt -lt $MaxRetries)

    return $result
}
