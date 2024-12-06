# Define parameters
$subscriptionId = Read-Host "Subscription Id"
$resourceGroupName = Read-Host "Resource group name"
$tenantId = Read-Host "Tenant Id"
$workspaceName = Read-Host "Workspace name"
$eventSubscriptionName = Read-Host "Event subscription name"
$eventHubNamespace = Read-Host "Event hub namespace"
$eventHubName = Read-Host "Event hub name"
$eventHubResourceGroup = Read-Host "Event hub resource group name"

# Authenticate with Azure (if not already authenticated)
Connect-AzAccount -UseDeviceAuthentication

# Set the Azure subscription
Set-AzContext -SubscriptionId $subscriptionId -TenantId $tenantId

# Get the Health Data Workspace resource
$workspace = Get-AzResource -ResourceGroupName $resourceGroupName -ResourceType "Microsoft.HealthcareApis/workspaces" -Name $workspaceName

# Get the Event Hub resource ID
$eventHub = Get-AzEventHub -ResourceGroupName $eventHubResourceGroup -NamespaceName $eventHubNamespace -EventHubName $eventHubName
$eventHubResourceId = $eventHub.Id

# Log and display the eventHubResourceId ID
Write-Output "eventHubResourceId : $eventHubResourceId"

# Create Event Hub Destination
$eventHubDestination = New-AzEventGridEventHubEventSubscriptionDestinationObject -ResourceId $eventHubResourceId


# Get the Azure Health Data Workspace ID
$workspaceId = "/subscriptions/$subscriptionId/resourceGroups/$resourceGroupName/providers/Microsoft.HealthcareApis/workspaces/$workspaceName"

# Log and display the workspace ID
Write-Output "Workspace ID: $workspaceId"

# Start Timer and Create Event Grid Subscription
$startTime = [datetime]::Now

# Define Event Types to Filter
$eventTypes = @(
    'Microsoft.HealthcareApis.FhirResourceCreated',
    'Microsoft.HealthcareApis.FhirResourceUpdated',
    'Microsoft.HealthcareApis.FhirResourceDeleted'
)

# Create the Event Grid subscription
New-AzEventGridSubscription -Name $eventSubscriptionName `
                            -Scope $workspaceId `
                            -Destination $eventHubDestination `
                            -FilterIncludedEventType  $eventTypes

# Check Provisioning States for Event Grid Subscription and Health Data Workspace
$eventGridProvisioningState = ""
$workspaceProvisioningState = ""

while ($eventGridProvisioningState -ne "Succeeded" -or $workspaceProvisioningState -ne "Succeeded") {
    Start-Sleep -Seconds 5

    # Check Event Grid Subscription Provisioning State
    $eventGridSubscription = Get-AzEventGridSubscription -Name $eventSubscriptionName -Scope $workspaceId 
    $eventGridProvisioningState = $eventGridSubscription.ProvisioningState
    #Write-Output "Event Grid Provisioning State: $eventGridProvisioningState"

    # Check Health Data Workspace Provisioning State
    $workspace = Get-AzResource -ResourceId $workspaceId
    $workspaceProvisioningState = $workspace.Properties.provisioningState
    Write-Output "Workspace Provisioning State: $workspaceProvisioningState"
}

# End Timer and Display Total Time
$endTime = [datetime]::Now
$totalTime = $endTime - $startTime
$totalMinutes = $totalTime.TotalSeconds / 60  # Convert seconds to minutes

Write-Output "Event Grid subscription for Health Data workspace enabled successfully. Time taken: $($totalTime.TotalSeconds) seconds."
Write-Output "Event Grid subscription for Health Data workspace enabled successfully. Time taken: $([math]::Round($totalMinutes, 2)) minutes."


# verify the event state of the all the FHIR services under given workspace
Write-Output "Verify the event state of the all the FHIR services under given workspace"


# Get all FHIR services under the specified Health Data workspace
$fhirServices = Get-AzResource -ResourceGroupName $resourceGroupName -ResourceType "Microsoft.HealthcareApis/workspaces/fhirservices" | 
                Where-Object { $_.ResourceId -like "$workspaceId*" }

# Display the count of FHIR services
Write-Output "Total FHIR services under the specified workspace: $($fhirServices.Count)"

# Flag to track the event state
$allServicesEnabled = $true
$enabledCounter = 0

# Loop through each FHIR service to check the event state
foreach ($fhirService in $fhirServices) {
    # Get the FHIR service details
    $fhirServiceDetails = Get-AzResource -ResourceId $fhirService.ResourceId -ExpandProperties

    # Access the event state property (adjusting for any naming conventions)
    $eventState = $fhirServiceDetails.Properties.eventState

    # Check the event state
    if ($eventState -eq "Enabled") {
        $enabledCounter++
        Write-Host "$enabledCounter. FHIR Service '$($fhirService.Name)': Event state is Enabled."
    } else {
        Write-Host "FHIR Service '$($fhirService.Name)': Event state is Disabled or Unavailable."
        $allServicesEnabled = $false
        break
    }
}

# Final status message
if ($allServicesEnabled) {
    Write-Host "All FHIR services event states are enabled."
} else {
    Write-Host "Failure: At least one FHIR service event state is disabled or unavailable."
}