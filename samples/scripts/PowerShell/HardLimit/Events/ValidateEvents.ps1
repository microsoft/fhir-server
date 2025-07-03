# Variables
$subscriptionId = Read-Host "Subscription Id"
$resourceGroupName = Read-Host "Resource group name"
$tenantId = Read-Host "Tenant Id"
$workspaceName = Read-Host "Workspace name"
$clientId = Read-Host "Client Id of App registration"
$clientSecret = Read-Host "Client secret" -AsSecureString  # Secure this appropriately
$jsonFilePath = Join-Path -Path $PSScriptRoot -ChildPath 'Resource.json'  # Local file path to the FHIR resource JSON
$resourceType = "Patient" # Type of FHIR resource

# Authenticate with Azure (if not already authenticated)
Connect-AzAccount -UseDeviceAuthentication

# Set the Azure subscription
Set-AzContext -SubscriptionId $subscriptionId -TenantId $tenantId

# Construct the resource ID for the workspace
$workspaceResourceId = "/subscriptions/$subscriptionId/resourceGroups/$resourceGroupName/providers/Microsoft.HealthcareApis/workspaces/$workspaceName"

# Read Resource JSON from the local file
$fhirResourceJson = Get-Content -Path $jsonFilePath -Raw

# Get all FHIR services under the specified Health Data workspace
$fhirServices = Get-AzResource -ResourceGroupName $resourceGroupName -ResourceType "Microsoft.HealthcareApis/workspaces/fhirservices" | 
                Where-Object { $_.ResourceId -like "$workspaceResourceId*" }

# Initialize the loop counter
$count = 0

# Loop through each FHIR instance to generate an access token and make the HTTP request
foreach ($fhirService in $fhirServices) {

    # Replace '/' with '-' in the FHIR service name
    $fhirServiceName = $fhirService.Name -replace '/', '-'
    Write-Output "FHIR Service Name: '$($fhirServiceName)'"

    # Generate Bearer Token for each FHIR instance
    $authBody = @{
        "grant_type" = "client_credentials"
        "client_id" = $clientId
        "client_secret" = $clientSecret
        "scope" = "https://$($fhirServiceName).fhir.azurehealthcareapis.com/.default"  # Scope for the FHIR service
    }
    $tokenResponse = Invoke-RestMethod -Uri "https://login.microsoftonline.com/$tenantId/oauth2/v2.0/token" -Method Post -Body $authBody -ContentType "application/x-www-form-urlencoded"
    $bearerToken = $tokenResponse.access_token

    # Define the FHIR service URL
    $fhirServiceUrl = "https://$($fhirServiceName).fhir.azurehealthcareapis.com"
    Write-Output "FHIR Service URL: '$($fhirServiceUrl)'"

    # Make the HTTP POST request with the bearer token as authorization
    try {
        # Use Invoke-WebRequest to access the StatusCode directly
        $response = Invoke-WebRequest -Uri "$fhirServiceUrl/$resourceType" -Method Post -Body $fhirResourceJson -ContentType "application/fhir+json" -Headers @{ Authorization = "Bearer $bearerToken" }
        
        # Check if the request was successful
        if ($response.StatusCode -eq 201) {
            Write-Output "FHIR resource created successfully in instance '$($fhirServiceName)'. Status Code: 201"
        } else {
            Write-Output "FHIR resource creation response in instance '$($fhirServiceName)'. Status Code: $($response.StatusCode)"
        }

        # Increment the counter on success
        $count++
        Write-Output "Processed FHIR instance count: $count"
    }
    catch {
        Write-Output "Error posting FHIR resource to instance '$($fhirServiceName)': $_"
    }
}

# Final completion message
Write-Output "Resource POST request complete for all FHIR services. Total processed: $count"