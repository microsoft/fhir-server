function Invoke-TimedStep {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [scriptblock]$ScriptBlock
    )

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    Write-Host "Starting $Name..."
    try {
        & $ScriptBlock
    }
    finally {
        $stopwatch.Stop()
        Write-Host "Completed $Name in $([Math]::Round($stopwatch.Elapsed.TotalSeconds, 1)) seconds."
    }
}

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

    $testAuthEnvironment = Get-Content -Raw -Path $TestAuthEnvironmentPath | ConvertFrom-Json

    $keyVaultResourceId = Invoke-TimedStep "Key Vault setup" {
        $keyVault = Get-AzKeyVault -VaultName $KeyVaultName -ResourceGroupName $ResourceGroupName

        if (!$keyVault) {
            Write-Host "Creating keyvault with the name $KeyVaultName"
            New-AzKeyVault -VaultName $KeyVaultName -ResourceGroupName $ResourceGroupName -Location $EnvironmentLocation | Out-Null
        }

        $retryCount = 0
        # Make sure key vault exists and is ready
        while (!(Get-AzKeyVault -VaultName $KeyVaultName -ResourceGroupName $ResourceGroupName)) {
            $retryCount += 1

            if ($retryCount -gt 20) {
                throw "Could not connect to the vault $KeyVaultName"
            }

            Write-Warning "Waiting on keyvault. Retry $retryCount"
            sleep 30
        }

        (Get-AzKeyVault -VaultName $KeyVaultName -ResourceGroupName $ResourceGroupName).ResourceId
    }

    $parameters = @{
        Name = 'AzureVault'
        ModuleName = 'Az.KeyVault'
        VaultParameters = @{
            AZKVaultName = $KeyVaultName
            SubscriptionId = (Get-AzContext).Subscription.Id
        }
        DefaultVault = $true
    }

    # Register the vault to store the secret values
    Register-SecretVault @parameters

    Invoke-TimedStep "Key Vault access setup" {
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
            Write-Host "Current context is account of type '$($azContext.Account.Type)' with id of '$($azContext.Account.Id)'"
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
    }

    $fhirServiceAudience = Get-ServiceAudience -ServiceName $EnvironmentName -TenantId $TenantId

    $ClientSecretCredential = New-Object -TypeName System.Management.Automation.PSCredential -ArgumentList $ClientId, $ClientSecret

    $application = Invoke-TimedStep "API application setup" {
        # Connect to Microsoft Graph using the credentials
        Connect-MgGraph -TenantId $tenantId -ClientSecretCredential $ClientSecretCredential -NoWelcome | Out-Null

        $apiApplication = Get-AzureAdApplicationByIdentifierUri $fhirServiceAudience

        if (!$apiApplication) {
            New-FhirServerApiApplicationRegistration -FhirServiceAudience $fhirServiceAudience | Out-Null

            # Change to use applicationId returned
            $apiApplication = Get-AzureAdApplicationByIdentifierUri $fhirServiceAudience
        }

        $apiApplication
    }

    Invoke-TimedStep "API application role setup" {
        Write-Host "Setting roles on API Application"

        # 1 - Setting up roles
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

        if ($appRoles.length -gt 0) {
            $appRoles = $appRoles | Select-Object -Unique
            Set-FhirServerApiApplicationRoles -ApiAppId $application.AppId -AppRoles $appRoles | Out-Null
        }
    }

    # 2 - Validating users
    $environmentUsers = @()
    if ($testAuthEnvironment.users -and $testAuthEnvironment.users.length -gt 0) {
        $environmentUsers = @(Invoke-TimedStep "User setup" {
            Write-Host "Ensuring users and role assignments for API Application exist"
            Set-FhirServerApiUsers -UserNamePrefix $EnvironmentName -TenantDomain $tenantInfo.TenantDomain -ApiAppId $application.AppId -UserConfiguration $testAuthEnvironment.users -KeyVaultName $KeyVaultName
        })
    }

    # 3 - Validating client applications
    $environmentClientApplications = @()
    if ($testAuthEnvironment.clientApplications -and $testAuthEnvironment.clientApplications.length -gt 0) {
        $environmentClientApplications = @(Invoke-TimedStep "Client application setup" {
            Write-Host "Ensuring client application exists"

            $clientApplications = @()
            foreach ($clientApp in $testAuthEnvironment.clientApplications) {
                $displayName = Get-ApplicationDisplayName -EnvironmentName $EnvironmentName -AppId $clientApp.Id
                $mgClientApplication = Get-AzureAdApplicationByDisplayName $displayName

                $publicClient = -not $clientApp.roles
                $appSecretName = "app--$($clientApp.Id)--secret"
                $appSecretKeyIdName = "app--$($clientApp.Id)--secret-keyid"
                $secretSecureString = $null
                $secretKeyIdSecureString = $null

                if (!$mgClientApplication) {
                    Write-Host "Creating client application $displayName."
                    $mgClientApplication = New-FhirServerClientApplicationRegistration -ApiAppId $application.AppId -DisplayName "$displayName" -PublicClient:$publicClient
                    Set-Secret -Name secretSecure -Secret $mgClientApplication.AppSecret
                    $secretSecureString = Get-Secret -Name secretSecure
                    Set-Secret -Name secretKeyIdSecure -Secret "$($mgClientApplication.AppSecretKeyId)"
                    $secretKeyIdSecureString = Get-Secret -Name secretKeyIdSecure
                }
                else {
                    $existingAppSecret = Get-AzKeyVaultSecret -VaultName $KeyVaultName -Name $appSecretName -ErrorAction SilentlyContinue
                    $existingAppSecretKeyId = Get-AzKeyVaultSecret -VaultName $KeyVaultName -Name $appSecretKeyIdName -ErrorAction SilentlyContinue
                    $existingAppSecretKeyIdValue = $null
                    if ($existingAppSecretKeyId -and $existingAppSecretKeyId.SecretValue) {
                        $existingAppSecretKeyIdValue = ([System.Net.NetworkCredential]::new("", $existingAppSecretKeyId.SecretValue).Password).ToString()
                    }

                    $mgApplication = Get-MgApplication -ApplicationId $mgClientApplication.Id -Property @('id', 'appId', 'passwordCredentials')
                    $passwordCredentials = @($mgApplication.PasswordCredentials)
                    $matchingPasswordCredential = $passwordCredentials | Where-Object { ([string]$_.KeyId) -eq $existingAppSecretKeyIdValue -and (!$_.EndDateTime -or $_.EndDateTime -gt (Get-Date).ToUniversalTime()) } | Select-Object -First 1

                    if ($existingAppSecret -and $existingAppSecret.SecretValue -and $matchingPasswordCredential) {
                        Write-Host "Client application $displayName already has a Key Vault secret tied to a non-expired password credential. Reusing existing secret."
                        $secretSecureString = $existingAppSecret.SecretValue
                        $secretKeyIdSecureString = $existingAppSecretKeyId.SecretValue
                    }
                    else {
                        Write-Host "Client application $displayName is missing a reusable Key Vault secret or matching non-expired password credential. Creating a new credential."
                        $passwordCredential = @{
                            displayName = "Generated by Add-AadTestAuthEnvironment"
                        }
                        $newPassword = Add-MgApplicationPassword -ApplicationId $mgClientApplication.Id -PasswordCredential $passwordCredential

                        Set-Secret -Name secretSecure -Secret $newPassword.SecretText
                        $secretSecureString = Get-Secret -Name secretSecure
                        Set-Secret -Name secretKeyIdSecure -Secret "$($newPassword.KeyId)"
                        $secretKeyIdSecureString = Get-Secret -Name secretKeyIdSecure
                    }
                }

                if ($publicClient) {
                    Grant-ClientAppDelegatedPermissions -AppId $mgClientApplication.AppId -TenantAdminCredential $TenantAdminCredential -ResourceApplicationId $application.AppId | Out-Null

                    # The public client (native app) is being used as SMART on FHIR client app in testing.
                    New-FhirServerSmartClientReplyUrl -AppId $mgClientApplication.AppId -FhirServerUrl $fhirServiceAudience -ReplyUrl "https://localhost:6001/sampleapp/index.html" | Out-Null
                }

                $clientApplications += @{
                    id          = $clientApp.Id
                    displayName = $displayName
                    appId       = $mgClientApplication.AppId
                }

                Set-Secret -Name appIdSecure -Secret $mgClientApplication.AppId
                $appIdSecureString = Get-Secret -Name appIdSecure
                Set-AzKeyVaultSecret -VaultName $KeyVaultName -Name "app--$($clientApp.Id)--id" -SecretValue $appIdSecureString | Out-Null
                Set-AzKeyVaultSecret -VaultName $KeyVaultName -Name $appSecretName -SecretValue $secretSecureString | Out-Null
                Set-AzKeyVaultSecret -VaultName $KeyVaultName -Name $appSecretKeyIdName -SecretValue $secretKeyIdSecureString | Out-Null

                Set-FhirServerClientAppRoleAssignments -ApiAppId $application.AppId -AppId $mgClientApplication.AppId -AppRoles $clientApp.roles | Out-Null
            }

            $clientApplications
        })
    }

    @{
        keyVaultName                  = $KeyVaultName
        environmentUsers              = $environmentUsers
        environmentClientApplications = $environmentClientApplications
    }
}
