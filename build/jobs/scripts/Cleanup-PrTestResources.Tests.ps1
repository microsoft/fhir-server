BeforeAll {
    $scriptUnderTest = Join-Path $PSScriptRoot 'Cleanup-PrTestResources.ps1'

    function Get-AzResourceGroup {
        [CmdletBinding()]
        param([string] $Name)
        throw 'Azure calls must be mocked.'
    }

    function Get-AzResource {
        [CmdletBinding()]
        param([string] $ResourceGroupName, [string] $ResourceId, [string] $ApiVersion)
        throw 'Azure calls must be mocked.'
    }

    function Remove-AzResource {
        [CmdletBinding()]
        param([string] $ResourceId, [switch] $Force, [string] $ApiVersion)
        throw 'Azure calls must be mocked.'
    }
}

Describe 'PR stage resource cleanup' {
    BeforeEach {
        $parameters = @{
            ResourceGroupName = 'msh-fhir-pr-123-456'
            DeploymentEnvironmentName = 'f123pr123-456'
            PrNumber = '123'
            BuildId = '456'
        }
        $groupId = '/subscriptions/test-subscription/resourceGroups/msh-fhir-pr-123-456'
        $environmentName = 'f123pr123-456'
        $nspId = "$groupId/providers/Microsoft.Network/networkSecurityPerimeters/nsp-msh-fhir-pr-123-456"
        $resourceIds = @(
            "$groupId/providers/Microsoft.Sql/servers/$environmentName"
            "$groupId/providers/Microsoft.Sql/servers/${environmentName}inttest"
            "$groupId/providers/Microsoft.Sql/servers/${environmentName}inttest/databases/fixture-db"
            "$groupId/providers/Microsoft.App/managedEnvironments/$environmentName-acae"
            "$groupId/providers/Microsoft.Storage/storageAccounts/shared"
            "$groupId/providers/Microsoft.KeyVault/vaults/shared"
            $nspId
        )
        $associationIds = @()
        foreach ($fixtureVersion in @('Stu3', 'R4', 'R5')) {
            $suffix = if ($fixtureVersion -eq 'Stu3') { '' } else { "-$($fixtureVersion.ToLowerInvariant())" }
            $appName = "$environmentName$suffix"
            $resourceIds += "$groupId/providers/Microsoft.App/containerApps/$appName-sql"
            $resourceIds += "$groupId/providers/Microsoft.Sql/servers/$environmentName/databases/FHIR$fixtureVersion"
            if ($fixtureVersion -ne 'R5') {
                $resourceIds += "$groupId/providers/Microsoft.App/containerApps/$appName"
                $resourceIds += "$groupId/providers/Microsoft.DocumentDB/databaseAccounts/$appName"
                $associationIds += "$nspId/resourceAssociations/cosmosdb-$appName-association"
            }
        }

        $state = @{
            Inventory = @($resourceIds | ForEach-Object { [pscustomobject]@{ ResourceId = $_ } })
            Associations = @($associationIds | ForEach-Object { [pscustomobject]@{ ResourceId = $_ } })
            Deleted = [System.Collections.Generic.List[string]]::new()
            Attempts = 0
        }

        Mock Get-AzResourceGroup { [pscustomobject]@{ ResourceId = $groupId } }
        Mock Get-AzResource { $state.Inventory } -ParameterFilter { $ResourceGroupName }
        Mock Get-AzResource { $state.Associations } -ParameterFilter { $ResourceId -eq "$nspId/resourceAssociations" }
        Mock Remove-AzResource { $state.Deleted.Add($ResourceId) }
        Mock Start-Sleep {}
    }

    It 'deletes only the <Version> <DataStore> app and datastore, in dependency order' -ForEach @(
        @{ Version = 'Stu3'; DataStore = 'sql'; AppSuffix = '-sql' }
        @{ Version = 'R4'; DataStore = 'sql'; AppSuffix = '-r4-sql' }
        @{ Version = 'R5'; DataStore = 'sql'; AppSuffix = '-r5-sql' }
        @{ Version = 'Stu3'; DataStore = 'cosmos'; AppSuffix = '' }
        @{ Version = 'R4'; DataStore = 'cosmos'; AppSuffix = '-r4' }
    ) {
        $appName = "$environmentName$AppSuffix"
        $expected = @("$groupId/providers/Microsoft.App/containerApps/$appName")
        if ($DataStore -eq 'sql') {
            $expected += "$groupId/providers/Microsoft.Sql/servers/$environmentName/databases/FHIR$Version"
        } else {
            $expected += "$nspId/resourceAssociations/cosmosdb-$appName-association"
            $expected += "$groupId/providers/Microsoft.DocumentDB/databaseAccounts/$appName"
        }

        & $scriptUnderTest @parameters -Version $Version -DataStore $DataStore

        @($state.Deleted) | Should -BeExactly $expected
        Should -Invoke Remove-AzResource -Times $expected.Count -Exactly -ParameterFilter { $Force -and $ErrorAction -eq 'Stop' }
        if ($DataStore -eq 'cosmos') {
            Should -Invoke Remove-AzResource -Times 1 -Exactly -ParameterFilter {
                $ResourceId -like '*/resourceAssociations/*' -and $ApiVersion -eq '2023-08-01-preview'
            }
        }
    }

    It 'does nothing when the stage resources are already absent' {
        $state.Inventory = @()

        & $scriptUnderTest @parameters -Version R4 -DataStore cosmos

        Should -Invoke Remove-AzResource -Times 0 -Exactly
    }

    It 'finishes SQL cleanup when a previous attempt already deleted the app' {
        $state.Inventory = @($state.Inventory | Where-Object {
            $_.ResourceId -ne "$groupId/providers/Microsoft.App/containerApps/$environmentName-r4-sql"
        })

        & $scriptUnderTest @parameters -Version R4 -DataStore sql

        @($state.Deleted) | Should -BeExactly @("$groupId/providers/Microsoft.Sql/servers/$environmentName/databases/FHIRR4")
    }

    It 'retries a transient deletion failure without deleting other stage resources' {
        Mock Remove-AzResource {
            $state.Attempts++
            if ($state.Attempts -eq 1) {
                throw 'Transient failure'
            }
            $state.Deleted.Add($ResourceId)
        }

        & $scriptUnderTest @parameters -Version R5 -DataStore sql

        $state.Attempts | Should -Be 3
        @($state.Deleted) | Should -BeExactly @(
            "$groupId/providers/Microsoft.App/containerApps/$environmentName-r5-sql"
            "$groupId/providers/Microsoft.Sql/servers/$environmentName/databases/FHIRR5"
        )
    }

    It 'fails visibly if deletion keeps failing and does not delete the backing database' {
        Mock Remove-AzResource { throw 'Deletion denied' }

        { & $scriptUnderTest @parameters -Version R4 -DataStore sql } | Should -Throw '*Deletion denied*'

        Should -Invoke Remove-AzResource -Times 5 -Exactly -ParameterFilter {
            $ResourceId -eq "$groupId/providers/Microsoft.App/containerApps/$environmentName-r4-sql"
        }
        Should -Invoke Remove-AzResource -Times 0 -Exactly -ParameterFilter {
            $ResourceId -like '*/databases/*'
        }
    }

    It 'does not delete the Cosmos account if its NSP association cannot be removed' {
        Mock Remove-AzResource { throw 'Association removal denied' } -ParameterFilter {
            $ResourceId -like '*/resourceAssociations/*'
        }

        { & $scriptUnderTest @parameters -Version Stu3 -DataStore cosmos } | Should -Throw '*Association removal denied*'

        Should -Invoke Remove-AzResource -Times 0 -Exactly -ParameterFilter {
            $ResourceId -like '*/Microsoft.DocumentDB/databaseAccounts/*'
        }
    }

    It 'does not interpret an inventory failure as absent resources' {
        Mock Get-AzResource { throw 'Inventory denied' } -ParameterFilter { $ResourceGroupName }

        { & $scriptUnderTest @parameters -Version Stu3 -DataStore sql } | Should -Throw '*Inventory denied*'

        Should -Invoke Remove-AzResource -Times 0 -Exactly
    }

    It 'rejects <Case> before accessing Azure' -ForEach @(
        @{ Case = 'a shared CI resource group'; Parameter = 'ResourceGroupName'; Value = 'msh-fhir-ci' }
        @{ Case = 'another build resource group'; Parameter = 'ResourceGroupName'; Value = 'msh-fhir-pr-123-789' }
        @{ Case = 'another build deployment'; Parameter = 'DeploymentEnvironmentName'; Value = 'f123pr123-789' }
    ) {
        $parameters[$Parameter] = $Value

        { & $scriptUnderTest @parameters -Version R4 -DataStore sql } | Should -Throw

        Should -Invoke Get-AzResourceGroup -Times 0 -Exactly
        Should -Invoke Remove-AzResource -Times 0 -Exactly
    }
}
