function Add-AadTestAuthEnvironment {
    <#
    .SYNOPSIS
    Adds all the required components for the test environment in AAD.
    .DESCRIPTION
    .PARAMETER TestAuthEnvironmentPath
    Path for the testauthenvironment.json file
    .PARAMETER EnvironmentName
    Environment name used for the test environment. This is used throughout for making names unique.
    .PARAMETER TenantAdminCredential
    Credentials for a tenant admin user. Needed to grant admin consent to client apps.
    #>
    param
    (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$TestAuthEnvironmentPath,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$EnvironmentName,

        [Parameter(Mandatory = $false)]
        [string]$EnvironmentLocation = "West US",

        [Parameter(Mandatory = $true)]
        [ValidateNotNull()]
        [pscredential]$TenantAdminCredential,

        [Parameter(Mandatory = $true )]
        [ValidateNotNullOrEmpty()]
        [String]$TenantId,

        [Parameter(Mandatory = $false)]
        [string]$ResourceGroupName = $EnvironmentName,

        [parameter(Mandatory = $false)]
        [string]$KeyVaultName = "$EnvironmentName-ts".ToLower(),

        [Parameter(Mandatory = $true )]
        [ValidateNotNullOrEmpty()]
        [String]$ClientId,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [SecureString]$ClientSecret
    )

    Set-StrictMode -Version Latest

    # Get current Microsoft Graph context
    try {
        $context = Get-MgContext -ErrorAction Stop
        if (-not $context) {
            throw "No Microsoft Graph session found"
        }
        # Get organization info to extract tenant domain
        $organization = Get-MgOrganization | Select-Object -First 1
        $tenantInfo = @{
            TenantDomain = $organization.VerifiedDomains | Where-Object { $_.IsDefault -eq $true } | Select-Object -ExpandProperty Name
        }
    }
    catch {
        throw "Please log in to Microsoft Graph with Connect-MgGraph cmdlet before proceeding"
    }

    # Get current Az context
    try {
        $azContext = Get-AzContext
    }
    catch {
        throw "Please log in to Azure RM with Login-AzAccount cmdlet before proceeding"
    }

    Write-Host "Setting up Test Authorization Environment for Microsoft Graph"

    # "AAD setup timing:" lines show where setup time goes so pipeline runs can be compared. They
    # log only durations and test-configuration keys, never secrets, Graph identifiers, or
    # response content.
    function Step-AadSetupTimer {
        param(
            [Parameter(Mandatory = $true)]
            [System.Diagnostics.Stopwatch]$Timer
        )

        $elapsedMs = $Timer.ElapsedMilliseconds
        $Timer.Restart()
        return $elapsedMs
    }

    $setupTimer = [System.Diagnostics.Stopwatch]::StartNew()
    $phaseTimer = [System.Diagnostics.Stopwatch]::StartNew()

    $testAuthEnvironment = Get-Content -Raw -Path $TestAuthEnvironmentPath | ConvertFrom-Json

    $keyVault = Get-AzKeyVault -VaultName $KeyVaultName -ResourceGroupName $ResourceGroupName

    if (!$keyVault) {
        Write-Host "Creating keyvault with the name $KeyVaultName"
        New-AzKeyVault -VaultName $KeyVaultName -ResourceGroupName $ResourceGroupName -Location $EnvironmentLocation | Out-Null
    }

    $retryCount = 0
    # Make sure key vault exists and is ready
    while (!(Get-AzKeyVault -VaultName $KeyVaultName -ResourceGroupName $ResourceGroupName )) {
        $retryCount += 1

        if ($retryCount -gt 20) {
            throw "Could not connect to the vault $KeyVaultName"
        }

        Write-Warning "Waiting on keyvault. Retry $retryCount"
        sleep 30
    }

    $keyVaultResourceId = (Get-AzKeyVault -VaultName $KeyVaultName -ResourceGroupName $ResourceGroupName).ResourceId
    Write-Host "AAD setup timing: phase=keyVault elapsedMs=$(Step-AadSetupTimer $phaseTimer) totalMs=$($setupTimer.ElapsedMilliseconds)"

      $parameters = @{
        Name = 'AzureVault'
        ModuleName = 'Az.KeyVault'
        VaultParameters = @{
            AZKVaultName = $KeyVaultName
            SubscriptionId = (Get-AzContext).Subscription.Id
        }
        DefaultVault = $true
    }

    # A task retry can reuse the registration from its previous attempt.
    $registeredVault = Get-SecretVault -ErrorAction Stop | Where-Object Name -eq $parameters.Name
    if ($registeredVault) {
        if ($registeredVault.ModuleName -ne $parameters.ModuleName -or
            $registeredVault.VaultParameters.AZKVaultName -ne $KeyVaultName -or
            $registeredVault.VaultParameters.SubscriptionId -ne $parameters.VaultParameters.SubscriptionId) {
            throw "Registered secret vault '$($parameters.Name)' does not match the intended Azure Key Vault."
        }

        if (-not $registeredVault.IsDefault) {
            Set-SecretVaultDefault -Name $parameters.Name -ErrorAction Stop
        }
    }
    else {
        Register-SecretVault @parameters -ErrorAction Stop
    }
    Write-Host "AAD setup timing: phase=secretVaultRegistration elapsedMs=$(Step-AadSetupTimer $phaseTimer) totalMs=$($setupTimer.ElapsedMilliseconds)"

    Write-Host "Setting permissions on keyvault for current context"
    if ($azContext.Account.Type -eq "User") {
        Write-Host "Current context is user: $($azContext.Account.Id)"
        $currentObjectId = (Get-AzADUser -UserPrincipalName $azContext.Account.Id).Id
    }
    elseif ($azContext.Account.Type -eq "ServicePrincipal") {
        Write-Host "Current context is service principal: $($azContext.Account.Id)"
        $currentObjectId = (Get-AzADServicePrincipal -ServicePrincipalName $azContext.Account.Id).Id
    }
    elseif ($azContext.Account.Type -eq "ClientAssertion") {
        Write-Host "Current context is ClientAssertion: $($azContext.Account.Id)"
        $currentObjectId = (Get-AzADServicePrincipal -ServicePrincipalName $azContext.Account.Id).Id
    }
    else {
        Write-Host "Current context is account of type '$($azContext.Account.Type)' with id of '$($azContext.Account.Id)"
        throw "Running as an unsupported account type. Please use either a 'User' or 'Service Principal' to run this command"
    }

    # Check if the role assignment already exists
    if ($currentObjectId) {
        $existingRoleAssignments = Get-AzRoleAssignment -ObjectId $currentObjectId -Scope $keyVaultResourceId
        $roleExists = $existingRoleAssignments | Where-Object { $_.RoleDefinitionName -eq "Key Vault Secrets Officer" }

        # Create the role assignment if it does not exist
        if (-not $roleExists) {
            Write-Host "Adding permission to keyvault for $currentObjectId"
            New-AzRoleAssignment -ObjectId $currentObjectId -RoleDefinitionName "Key Vault Secrets Officer" -Scope $keyVaultResourceId | Out-Null
        }
        else {
            Write-Host "Role assignment already exists for $currentObjectId"
        }
    }
    Write-Host "AAD setup timing: phase=keyVaultAccess elapsedMs=$(Step-AadSetupTimer $phaseTimer) totalMs=$($setupTimer.ElapsedMilliseconds)"

    Write-Host "Ensuring API application exists"

    $fhirServiceAudience = Get-ServiceAudience -ServiceName $EnvironmentName -TenantId $TenantId

    $ClientSecretCredential = New-Object -TypeName System.Management.Automation.PSCredential -ArgumentList $ClientId, $ClientSecret
        
    # Connect to Microsoft Graph using the credentials
    Connect-MgGraph -TenantId $tenantId -ClientSecretCredential $ClientSecretCredential
    Write-Host "AAD setup timing: phase=graphConnect elapsedMs=$(Step-AadSetupTimer $phaseTimer) totalMs=$($setupTimer.ElapsedMilliseconds)"

    # Set the final roles during registration to avoid immediately removing the default admin role.
    $appRoles = @()
    if ($testAuthEnvironment.users -and $testAuthEnvironment.users.length -gt 0) {
        $userRoles = $testAuthEnvironment.users | Where-Object { $_.roles } | ForEach-Object { $_.roles }
        if ($userRoles) {
            $appRoles += $userRoles
        }
    }
    
    if ($testAuthEnvironment.clientApplications -and $testAuthEnvironment.clientApplications.length -gt 0) {
        $clientRoles = $testAuthEnvironment.clientApplications | Where-Object { $_.roles } | ForEach-Object { $_.roles }
        if ($clientRoles) {
            $appRoles += $clientRoles
        }
    }
    
    $appRoles = @($appRoles | Select-Object -Unique)
    $application = Get-AzureAdApplicationByIdentifierUri $fhirServiceAudience
    $createdApplication = $false

    if (!$application) {
        $registrationParams = @{ FhirServiceAudience = $fhirServiceAudience }
        if ($appRoles.Length -gt 0) {
            $registrationParams.AppRoles = $appRoles
        }

        # Use the identity returned by the registration directly. Re-querying Microsoft Graph
        # immediately after creation can return nothing while the new application propagates,
        # which previously left $application null and failed later with
        # "The property 'AppId' cannot be found on this object".
        $application = New-FhirServerApiApplicationRegistration @registrationParams
        $createdApplication = $true
    }

    Write-Host "Setting roles on API Application"

    if ($appRoles.Length -gt 0 -and -not $createdApplication) {
        Set-FhirServerApiApplicationRoles -ApiAppId $application.AppId -AppRoles $appRoles | Out-Null
    }
    Write-Host "AAD setup timing: phase=apiApplication elapsedMs=$(Step-AadSetupTimer $phaseTimer) totalMs=$($setupTimer.ElapsedMilliseconds)"

    # 2 - Validating users
    $environmentUsers = @()
    if ($testAuthEnvironment.users -and $testAuthEnvironment.users.length -gt 0) {
        Write-Host "Ensuring users and role assignments for API Application exist"
        $environmentUsers = Set-FhirServerApiUsers -UserNamePrefix $EnvironmentName -TenantDomain $tenantInfo.TenantDomain -ApiAppId $application.AppId -UserConfiguration $testAuthEnvironment.users -KeyVaultName $KeyVaultName
    }
    Write-Host "AAD setup timing: phase=users elapsedMs=$(Step-AadSetupTimer $phaseTimer) totalMs=$($setupTimer.ElapsedMilliseconds)"

    # 3 - Validating client applications
    $environmentClientApplications = @()
    if ($testAuthEnvironment.clientApplications -and $testAuthEnvironment.clientApplications.length -gt 0) {
        Write-Host "Ensuring client application exists"
        $clientCount = @($testAuthEnvironment.clientApplications).Count
        $clientPosition = 0
        $totalScratchVaultMs = 0
        foreach ($clientApp in $testAuthEnvironment.clientApplications) {
            $clientPosition++
            $clientTimer = [System.Diagnostics.Stopwatch]::StartNew()
            $clientLapTimer = [System.Diagnostics.Stopwatch]::StartNew()
            $scratchVaultTimer = [System.Diagnostics.Stopwatch]::new()
            Write-Host "AAD setup timing: client=$($clientApp.Id) position=$clientPosition/$clientCount started"

            $displayName = Get-ApplicationDisplayName -EnvironmentName $EnvironmentName -AppId $clientApp.Id
            $mgClientApplication = Get-AzureAdApplicationByDisplayName $displayName
            $lookupMs = Step-AadSetupTimer $clientLapTimer

            $publicClient = -not $clientApp.roles
            $clientState = if ($mgClientApplication) { 'reused' } else { 'created' }

            if (!$mgClientApplication) {

                $mgClientApplication = New-FhirServerClientApplicationRegistration -ApiAppId $application.AppId -DisplayName "$displayName" -PublicClient:$publicClient

            $scratchVaultTimer.Start()
            Set-Secret -Name secretSecure -Secret $mgClientApplication.AppSecret
            $secretSecureString = Get-Secret -Name secretSecure
            $scratchVaultTimer.Stop()

        }
        else {
            # Remove existing password credentials and create new ones using Microsoft Graph
            $existingCredentials = Get-MgApplication -ApplicationId $mgClientApplication.Id | Select-Object -ExpandProperty PasswordCredentials
            if ($existingCredentials) {
                # Remove all existing password credentials
                foreach ($credential in $existingCredentials) {
                    Remove-MgApplicationPassword -ApplicationId $mgClientApplication.Id -KeyId $credential.KeyId
                }
            }
            
            # Create new password credential
            $passwordCredential = @{
                displayName = "Generated by Add-AadTestAuthEnvironment"
            }

            # A task retry runs alongside other tenant modifications, so Microsoft Graph can
            # reject the credential rotation with a transient 409 Directory_ConcurrencyViolation.
            # Retry that specific concurrency conflict with bounded backoff, surface any other
            # error immediately, and fail explicitly if the conflict never clears.
            $newPassword = $null
            for ($attempt = 1; $attempt -le 5; $attempt++) {
                try {
                    $newPassword = Add-MgApplicationPassword -ApplicationId $mgClientApplication.Id -PasswordCredential $passwordCredential -ErrorAction Stop
                    break
                }
                catch {
                    if ($attempt -eq 5 -or
                        ($_.FullyQualifiedErrorId -notlike 'Directory_ConcurrencyViolation*' -and
                         $_.Exception.Message -notlike '*Directory_ConcurrencyViolation*')) {
                        throw
                    }

                    Write-Warning "Microsoft Graph reported concurrent tenant modifications while rotating the client secret for $($mgClientApplication.Id) (attempt $attempt of 5)."
                    Start-Sleep -Seconds (5 * [math]::Pow(2, $attempt - 1))
                }
            }

            $scratchVaultTimer.Start()
            Set-Secret -Name secretSecure -Secret $newPassword.SecretText
            $secretSecureString = Get-Secret -Name secretSecure 
            $scratchVaultTimer.Stop()
        }
        $credentialMs = Step-AadSetupTimer $clientLapTimer

        if ($publicClient) {
            Grant-ClientAppDelegatedPermissions -AppId $mgClientApplication.AppId -TenantAdminCredential $TenantAdminCredential -ResourceApplicationId $application.AppId

            # The public client (native app) is being used as SMART on FHIR client app in testing.
            New-FhirServerSmartClientReplyUrl -AppId $mgClientApplication.AppId -FhirServerUrl $fhirServiceAudience -ReplyUrl "https://localhost:6001/sampleapp/index.html"
        }
        # Includes the SMART reply URL update; near zero for confidential clients.
        $delegatedGrantMs = Step-AadSetupTimer $clientLapTimer

        $environmentClientApplications += @{
            id          = $clientApp.Id
            displayName = $displayName
            appId       = $mgClientApplication.AppId
        }

        $scratchVaultTimer.Start()
        Set-Secret -Name appIdSecure -Secret $mgClientApplication.AppId
        $appIdSecureString = Get-Secret -Name appIdSecure
        $scratchVaultTimer.Stop()
        Set-AzKeyVaultSecret -VaultName $KeyVaultName -Name "app--$($clientApp.Id)--id" -SecretValue $appIdSecureString | Out-Null
        Set-AzKeyVaultSecret -VaultName $KeyVaultName -Name "app--$($clientApp.Id)--secret" -SecretValue $secretSecureString | Out-Null
        $vaultWriteMs = Step-AadSetupTimer $clientLapTimer

        Set-FhirServerClientAppRoleAssignments -ApiAppId $application.AppId -AppId $mgClientApplication.AppId -AppRoles $clientApp.roles | Out-Null
        $roleAssignmentMs = Step-AadSetupTimer $clientLapTimer

        # scratchVaultMs is the Set-Secret/Get-Secret time already counted in credentialMs and vaultWriteMs.
        $totalScratchVaultMs += $scratchVaultTimer.ElapsedMilliseconds
        Write-Host "AAD setup timing: client=$($clientApp.Id) position=$clientPosition/$clientCount state=$clientState public=$publicClient lookupMs=$lookupMs credentialMs=$credentialMs delegatedGrantMs=$delegatedGrantMs vaultWriteMs=$vaultWriteMs roleAssignmentMs=$roleAssignmentMs scratchVaultMs=$($scratchVaultTimer.ElapsedMilliseconds) totalMs=$($clientTimer.ElapsedMilliseconds)"
        }
        Write-Host "AAD setup timing: phase=clientApplications elapsedMs=$(Step-AadSetupTimer $phaseTimer) totalMs=$($setupTimer.ElapsedMilliseconds) clients=$clientCount scratchVaultMs=$totalScratchVaultMs"
    }

    @{
        keyVaultName                  = $KeyVaultName
        environmentUsers              = $environmentUsers
        environmentClientApplications = $environmentClientApplications
    }
}
