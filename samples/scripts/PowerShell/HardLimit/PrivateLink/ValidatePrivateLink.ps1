$subscriptionId = Read-Host "Subscription Id"
$resourceGroupName = Read-Host "Resource group name"
$maxRetries = 10  # Maximum number of retry attempts per endpoint
$sleepSeconds = 10  # Time to wait between retries
$maxParallelJobs = 10  # Maximum number of parallel jobs to run
$totalTimeout = 120  # Total timeout in minutes
 
# Connect to Azure if not already connected
Connect-AzAccount -UseDeviceAuthentication
Set-AzContext -SubscriptionId $subscriptionId
 
# Function to test a single FHIR service metadata endpoint
$testEndpointScript = {
    param (
        [string]$endpoint,
        [string]$fhirServiceName,
        [int]$maxRetries,
        [int]$sleepSeconds,
        [bool]$isInsideVnet
    )
    
    $startTime = Get-Date
    $retryCount = 0
    $finalResult = $null
    
    while ($retryCount -lt $maxRetries) {
        try {
            $response = curl $endpoint
            $statusCode = $response.StatusCode
            $success = $false
            $success = if ($isInsideVnet) {
                ($statusCode -eq 200)
            }
        }
        catch [System.Net.WebException] {
            if (-not $isInsideVnet){
                $success = ($_.Exception.Message -match "The remote name could not be resolved")
                $statusCode = 403
            }            
        }
        
        if ($success) {
            $finalResult = @{
                ServiceName = $fhirServiceName
                Endpoint = $endpoint
                FinalStatus = $statusCode
                Success = $true
                Attempts = $retryCount + 1
                TimeToComplete = (Get-Date) - $startTime
                Error = $null
                Environment = if ($isInsideVnet) { "Inside VNet" } else { "Outside VNet" }
            }
            break
        }
        
        $retryCount++
        if ($retryCount -lt $maxRetries) {
            Start-Sleep -Seconds $sleepSeconds
        }
    }
    
    if (-not $finalResult) {
        $finalResult = @{
            ServiceName = $fhirServiceName
            Endpoint = $endpoint
            FinalStatus = $statusCode
            Success = $false
            Attempts = $retryCount
            TimeToComplete = (Get-Date) - $startTime
            Error = "Max retries exceeded"
            Environment = if ($isInsideVnet) { "Inside VNet" } else { "Outside VNet" }
        }
    }
    
    return $finalResult
}
 
# Get all FHIR services in the workspace
$fhirServices = Get-AzResource -ResourceGroupName $resourceGroupName -ResourceType "Microsoft.HealthcareApis/workspaces/fhirservices" | 
                    Where-Object { $_.ResourceId -like "$workspaceResourceId*" }

Write-Host "Found $($fhirServices.Count) FHIR services to test."
 
# Function to run tests for a specific environment
function Start-EnvironmentTests {
    param (
        [array]$services,
        [bool]$isInsideVnet
    )
    
    $environmentName = if ($isInsideVnet) { "Inside VNet" } else { "Outside VNet" }
    Write-Host "`nStarting tests for environment: $environmentName" -ForegroundColor Cyan
    
    $jobs = @{}
    $results = @{}
    #$startTime = Get-Date
    
    foreach ($fhir in $services) {
        $metadataEndpoint = (Get-AzResource -ResourceId $fhir.ResourceId).properties.authenticationConfiguration.audience
        $metadataEndpoint = $metadataEndpoint.TrimEnd('/') + "/metadata"

        # Wait if we've hit the maximum parallel jobs limit
        while ((Get-Job -State Running).Count -ge $maxParallelJobs) {
            Start-Sleep -Seconds 5
            
            # Check completed jobs
            $completedJobs = Get-Job -State Completed
            foreach ($job in $completedJobs) {
                $result = Receive-Job -Job $job
                $results[$result.ServiceName] = $result
                $expectedStatus = if ($isInsideVnet) { "200" } else { "403" }
                Write-Host "[$environmentName] Completed testing $($result.ServiceName) - Status: $($result.FinalStatus) (Expected: $expectedStatus) - Time: $($result.TimeToComplete.TotalSeconds) seconds" -ForegroundColor $(if ($result.Success) { "Green" } else { "Red" })
                Remove-Job -Job $job
            }
        }
        
        # Start new job
$job = Start-Job -ScriptBlock $testEndpointScript -ArgumentList $metadataEndpoint, $fhir.Name, $maxRetries, $sleepSeconds, $isInsideVnet
$jobs[$fhir.Name] = $job
Write-Host "[$environmentName] Started testing $($fhir.Name)"
    }
    
    # Monitor remaining jobs
    $timeout = (Get-Date).AddMinutes($totalTimeout)
    while (Get-Job) {
        if ((Get-Date) -gt $timeout) {
            Write-Host "[$environmentName] Total timeout reached. Stopping remaining jobs..." -ForegroundColor Red
            Get-Job | Stop-Job
            Get-Job | Remove-Job
            break
        }
        
        $completedJobs = Get-Job -State Completed
        foreach ($job in $completedJobs) {
            $result = Receive-Job -Job $job
            $results[$result.ServiceName] = $result
            $expectedStatus = if ($isInsideVnet) { "200" } else { "403" }
            Write-Host "[$environmentName] Completed testing $($result.ServiceName) - Status: $($result.FinalStatus) (Expected: $expectedStatus) - Time: $($result.TimeToComplete.TotalSeconds) seconds" -ForegroundColor $(if ($result.Success) { "Green" } else { "Red" })
            Remove-Job -Job $job
        }
        
        if (Get-Job) {
            Write-Host "[$environmentName] Waiting for $($(Get-Job).Count) remaining jobs..."
            Start-Sleep -Seconds 5
        }
    }
    
    $endTime = Get-Date
    $duration = $endTime - $startTime
    
    return @{
        Results = $results
        Duration = $duration
        Environment = $environmentName
    }
}

$testResults = Start-EnvironmentTests -services $fhirServices -isInsideVnet $true

if ($testResults) {
    Write-Host "`n=== Results ===" -ForegroundColor Cyan
    Write-Host "Successfully Tested: $($testResults.Results.Values.Where({ $_.Success }).Count)"
    Write-Host "Failed or Timed Out: $($testResults.Results.Values.Where({ -not $_.Success }).Count)"
    Write-Host "Total Duration: $($testResults.Duration.TotalMinutes) minutes"
    Write-Host "Total Duration: $($testResults.Duration.TotalSeconds) seconds"
}