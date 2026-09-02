[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path "$PSScriptRoot/../../../..").Path
$resolver = Join-Path $repositoryRoot 'build/jobs/scripts/Resolve-AcaSqlDeploymentPlan.ps1'
$providerAssertion = Join-Path $repositoryRoot 'build/jobs/scripts/Assert-EffectiveFhirSdkProvider.ps1'
. $providerAssertion

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)] $Expected,
        [Parameter(Mandatory = $true)] $Actual,
        [Parameter(Mandatory = $true)] [string] $Description
    )

    if ($Expected -ne $Actual) {
        throw "$Description. Expected '$Expected', actual '$Actual'."
    }
}

function Assert-True {
    param(
        [Parameter(Mandatory = $true)] [bool] $Condition,
        [Parameter(Mandatory = $true)] [string] $Description
    )

    if (-not $Condition) {
        throw $Description
    }
}

function Assert-Throws {
    param(
        [Parameter(Mandatory = $true)] [scriptblock] $Action,
        [Parameter(Mandatory = $true)] [string] $ExpectedMessage,
        [Parameter(Mandatory = $true)] [string] $Description
    )

    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notlike "*$ExpectedMessage*") {
            throw "$Description. Expected error containing '$ExpectedMessage', actual '$($_.Exception.Message)'."
        }

        return
    }

    throw "$Description. Expected an exception."
}

function Get-PythonWithPyYaml {
    $pythonCommand = $null
    foreach ($commandName in @('python', 'python3')) {
        $pythonCommand = Get-Command $commandName -ErrorAction SilentlyContinue
        if ($null -ne $pythonCommand) {
            break
        }
    }

    if ($null -eq $pythonCommand) {
        throw 'Python is required to validate the SQL vNext deployment plan.'
    }

    & $pythonCommand.Source -c 'import sys' 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw 'Python is required to validate the SQL vNext deployment plan.'
    }

    & $pythonCommand.Source -c 'import yaml' 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "The Python package 'PyYAML' is required to validate the SQL vNext deployment plan."
    }

    return $pythonCommand.Source
}

function Assert-AzureCliBicepAvailable {
    $azureCli = Get-Command az -ErrorAction SilentlyContinue
    if ($null -eq $azureCli) {
        throw 'Azure CLI is required to compile the SQL vNext Bicep template.'
    }

    & $azureCli.Source bicep version | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Azure CLI Bicep is required to compile the SQL vNext Bicep template.'
    }
}

function ConvertFrom-YamlFile {
    param([Parameter(Mandatory = $true)] [string] $Path)

    $yaml = Get-Content -Raw $Path
    $json = $yaml | & $script:pythonPath -c 'import json, sys, yaml; json.dump(yaml.safe_load(sys.stdin), sys.stdout)'
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to parse YAML file '$Path'."
    }

    return $json | ConvertFrom-Json -Depth 100
}

function Get-Stage {
    param(
        [Parameter(Mandatory = $true)] $Pipeline,
        [Parameter(Mandatory = $true)] [string] $Name
    )

    $stage = @($Pipeline.stages | Where-Object { $_.stage -eq $Name })
    Assert-Equal -Expected 1 -Actual $stage.Count -Description "Stage '$Name' should occur once"
    return $stage[0]
}

function Get-ScriptArgument {
    param(
        [Parameter(Mandatory = $true)] [string] $Arguments,
        [Parameter(Mandatory = $true)] [string] $Name
    )

    $match = [regex]::Match($Arguments, "(?m)-$([regex]::Escape($Name))\s+`"([^`"]*)`"")
    if (-not $match.Success) {
        throw "Script argument '-$Name' was not found."
    }

    return $match.Groups[1].Value
}

function Find-TemplateInvocation {
    param(
        [Parameter(Mandatory = $true)] $Node,
        [Parameter(Mandatory = $true)] [string] $Template
    )

    if ($Node -is [pscustomobject]) {
        if ($Node.PSObject.Properties['template'] -and $Node.template -eq $Template) {
            Write-Output -NoEnumerate $Node
        }

        foreach ($property in $Node.PSObject.Properties) {
            Find-TemplateInvocation -Node $property.Value -Template $Template
        }
    } elseif ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string]) {
        foreach ($item in $Node) {
            Find-TemplateInvocation -Node $item -Template $Template
        }
    }
}

$script:pythonPath = Get-PythonWithPyYaml
Assert-AzureCliBicepAvailable

$topologyTests = Join-Path $repositoryRoot 'build/jobs/scripts/tests/Test-AcaSqlTopology.ps1'
& $topologyTests

$legacyPlan = & $resolver -Version Stu3
Assert-Equal -Expected 'FHIRStu3' -Actual $legacyPlan.SqlDatabaseName -Description 'Legacy database default changed'
Assert-Equal -Expected 'Firely' -Actual $legacyPlan.FhirSdkProviderDefault -Description 'Legacy SDK provider default changed'
Assert-Equal -Expected $false -Actual $legacyPlan.EmitFhirSdkProviderEnvironmentVariable -Description 'Legacy deployment would emit a new provider setting'

$configuredPlan = & $resolver -Version R4 -ConfiguredFhirSdkProviderDefault Ignixa
Assert-Equal -Expected 'Ignixa' -Actual $configuredPlan.FhirSdkProviderDefault -Description 'Configured provider was not preserved'
Assert-Equal -Expected $true -Actual $configuredPlan.EmitFhirSdkProviderEnvironmentVariable -Description 'Configured provider would not be emitted'

$configuredFirelyPlan = & $resolver -Version R4 -ConfiguredFhirSdkProviderDefault Firely
Assert-Equal -Expected $true -Actual $configuredFirelyPlan.EmitFhirSdkProviderEnvironmentVariable -Description 'Flattened Firely setting would be discarded'

Assert-Throws -Action {
    & $resolver -Version R4 -FhirSdkProviderDefault Ignixa -ConfiguredFhirSdkProviderDefault Firely
} -ExpectedMessage 'conflicts with configured provider' -Description 'Conflicting providers were accepted'

$stu3VNextPlan = & $resolver -Version Stu3 -SqlDatabaseName FHIRStu3VNext -FhirSdkProviderDefault Ignixa
$r4VNextPlan = & $resolver -Version R4 -SqlDatabaseName FHIRR4VNext -FhirSdkProviderDefault Ignixa
Assert-Equal -Expected 'FHIRStu3VNext' -Actual $stu3VNextPlan.SqlDatabaseName -Description 'STU3 vNext database is not isolated'
Assert-Equal -Expected 'FHIRR4VNext' -Actual $r4VNextPlan.SqlDatabaseName -Description 'R4 vNext database is not isolated'
Assert-Equal -Expected $true -Actual $stu3VNextPlan.EmitFhirSdkProviderEnvironmentVariable -Description 'STU3 vNext provider would not be emitted'
Assert-Equal -Expected $true -Actual $r4VNextPlan.EmitFhirSdkProviderEnvironmentVariable -Description 'R4 vNext provider would not be emitted'

$matchingEnvironment = @([pscustomobject]@{ name = 'FhirServer__CoreFeatures__FhirSdkProvider__Default'; value = 'Ignixa' })
Assert-EffectiveFhirSdkProvider -EnvironmentSettings $matchingEnvironment -ExpectedProvider Ignixa -ContainerAppName matching-app
Assert-EffectiveFhirSdkProvider -EnvironmentSettings @() -ExpectedProvider '' -ContainerAppName legacy-app
Assert-Throws -Action {
    Assert-EffectiveFhirSdkProvider -EnvironmentSettings @() -ExpectedProvider Ignixa -ContainerAppName missing-app
} -ExpectedMessage 'does not define' -Description 'Missing expected provider was accepted'
Assert-Throws -Action {
    Assert-EffectiveFhirSdkProvider -EnvironmentSettings $matchingEnvironment -ExpectedProvider Firely -ContainerAppName mismatch-app
} -ExpectedMessage "expected 'Firely'" -Description 'Mismatched provider was accepted'

$yamlPaths = @(
    'build/build-variables.yml',
    'build/ci-deploy.yml',
    'build/ci-pipeline.yml',
    'build/jobs/e2e-tests.yml',
    'build/jobs/provision-deploy.yml',
    'build/jobs/run-sql-tests.yml',
    'build/pr-pipeline.yml',
    'build/tasks/e2e-set-variables.yml'
)
$yamlDocuments = @{}
foreach ($relativePath in $yamlPaths) {
    $yamlDocuments[$relativePath] = ConvertFrom-YamlFile (Join-Path $repositoryRoot $relativePath)
}

$variables = $yamlDocuments['build/build-variables.yml'].variables
Assert-Equal -Expected '$(DeploymentEnvironmentName)-svn' -Actual $variables.DeploymentEnvironmentNameSqlVNext -Description 'STU3 vNext app name is not distinct'
Assert-Equal -Expected '$(DeploymentEnvironmentName)-r4vn' -Actual $variables.DeploymentEnvironmentNameR4SqlVNext -Description 'R4 vNext app name is not distinct'
Assert-Equal -Expected '$(KeyVaultBaseName)-sql-vn' -Actual $variables.KeyVaultNameSqlVNext -Description 'STU3 vNext Key Vault is not distinct'
Assert-Equal -Expected '$(KeyVaultBaseName)-r4-vn' -Actual $variables.KeyVaultNameR4SqlVNext -Description 'R4 vNext Key Vault is not distinct'

$prPipeline = $yamlDocuments['build/pr-pipeline.yml']
$ciDeployPipeline = $yamlDocuments['build/ci-deploy.yml']
$mainPipeline = $yamlDocuments['build/ci-pipeline.yml']

foreach ($pipelinePlan in @(
    @{ Pipeline = $prPipeline; Stu3Stage = 'deployStu3SqlVNext'; R4Stage = 'deployR4SqlVNext'; ResourceGroup = '$(UniqueResourceGroupName)' },
    @{ Pipeline = $ciDeployPipeline; Stu3Stage = 'deployStu3SqlVNext'; R4Stage = 'deployR4SqlVNext'; ResourceGroup = '$(ResourceGroupName)' }
)) {
    $stu3Parameters = (Get-Stage -Pipeline $pipelinePlan.Pipeline -Name $pipelinePlan.Stu3Stage).jobs[0].parameters
    $r4Parameters = (Get-Stage -Pipeline $pipelinePlan.Pipeline -Name $pipelinePlan.R4Stage).jobs[0].parameters

    Assert-Equal -Expected 'FHIRStu3VNext' -Actual $stu3Parameters.sqlDatabaseName -Description "$($pipelinePlan.Stu3Stage) database flow is incorrect"
    Assert-Equal -Expected 'FHIRR4VNext' -Actual $r4Parameters.sqlDatabaseName -Description "$($pipelinePlan.R4Stage) database flow is incorrect"
    Assert-Equal -Expected '$(SqlVNextElasticPoolName)' -Actual $stu3Parameters.sqlElasticPoolName -Description "$($pipelinePlan.Stu3Stage) pool flow is incorrect"
    Assert-Equal -Expected '$(SqlVNextElasticPoolName)' -Actual $r4Parameters.sqlElasticPoolName -Description "$($pipelinePlan.R4Stage) pool flow is incorrect"
    Assert-Equal -Expected $pipelinePlan.ResourceGroup -Actual $stu3Parameters.resourceGroup -Description "$($pipelinePlan.Stu3Stage) resource group is incorrect"
    Assert-Equal -Expected $pipelinePlan.ResourceGroup -Actual $r4Parameters.resourceGroup -Description "$($pipelinePlan.R4Stage) resource group is incorrect"
}

$prStu3SqlParameters = (Get-Stage -Pipeline $prPipeline -Name deployStu3Sql).jobs[0].parameters
$prR4SqlParameters = (Get-Stage -Pipeline $prPipeline -Name deployR4Sql).jobs[0].parameters
Assert-True -Condition (-not $prStu3SqlParameters.PSObject.Properties['sqlElasticPoolName']) -Description 'Existing PR STU3 database was moved to the vNext pool'
Assert-True -Condition (-not $prR4SqlParameters.PSObject.Properties['sqlElasticPoolName']) -Description 'Existing PR R4 database was moved to the vNext pool'

$ciStu3SqlParameters = (Get-Stage -Pipeline $ciDeployPipeline -Name deployStu3Sql).jobs[0].parameters
$ciR4SqlParameters = (Get-Stage -Pipeline $ciDeployPipeline -Name deployR4Sql).jobs[0].parameters
Assert-Equal -Expected '$(DeploymentEnvironmentName)-pool' -Actual $ciStu3SqlParameters.sqlElasticPoolName -Description 'Existing CI STU3 database pool changed'
Assert-Equal -Expected '$(DeploymentEnvironmentName)-pool' -Actual $ciR4SqlParameters.sqlElasticPoolName -Description 'Existing CI R4 database pool changed'

foreach ($pipeline in @($prPipeline, $ciDeployPipeline)) {
    $poolStage = Get-Stage -Pipeline $pipeline -Name deploySqlVNextElasticPool
    Assert-Equal -Expected 1 -Actual @($poolStage.jobs).Count -Description 'vNext pool stage contains unrelated jobs'
    $poolParameters = $poolStage.jobs[0].parameters
    Assert-Equal -Expected '$(SqlVNextElasticPoolName)' -Actual $poolParameters.elasticPoolName -Description 'vNext pool name is incorrect'
    Assert-Equal -Expected 4 -Actual $poolParameters.capacity -Description 'vNext pool capacity does not support both canaries'
    Assert-Equal -Expected 2 -Actual $poolParameters.dbMaxCapacity -Description 'vNext per-database cap is incorrect'
}
Assert-Equal -Expected 2 -Actual @((Get-Stage -Pipeline $prPipeline -Name deploySqlServer).jobs).Count -Description 'PR SQL server stage topology changed'

foreach ($stageName in @('redeployStu3SqlVNext', 'redeployR4SqlVNext')) {
    $stage = Get-Stage -Pipeline $mainPipeline -Name $stageName
    Assert-Equal -Expected './jobs/redeploy-webapp.yml' -Actual $stage.jobs[0].template -Description "$stageName does not use persistent redeployment"
}

foreach ($pipeline in @($prPipeline, $mainPipeline)) {
    $validationStage = Get-Stage -Pipeline $pipeline -Name AnalyzeSecurity
    $validationJob = @($validationStage.jobs | Where-Object { $_.job -eq 'ValidateAcaSqlDeploymentPlan' })
    Assert-Equal -Expected 1 -Actual $validationJob.Count -Description 'Deployment-plan validation job is not wired into CI'
    Assert-Equal -Expected '$(System.DefaultWorkingDirectory)/build/jobs/scripts/tests/Test-AcaSqlVNextDeploymentPlan.ps1' -Actual $validationJob[0].steps[0].inputs.filePath -Description 'CI validation runs the wrong deployment-plan test'
}

foreach ($pipeline in @($prPipeline, $mainPipeline)) {
    foreach ($testPlan in @(
        @{
            Stage = 'testStu3SqlVNext'
            KeyVault = '$(KeyVaultNameSqlVNext)'
            Database = 'FHIRStu3VNext'
        },
        @{
            Stage = 'testR4SqlVNext'
            KeyVault = '$(KeyVaultNameR4SqlVNext)'
            Database = 'FHIRR4VNext'
        }
    )) {
        $parameters = (Get-Stage -Pipeline $pipeline -Name $testPlan.Stage).jobs[0].parameters
        Assert-Equal -Expected $testPlan.KeyVault -Actual $parameters.expectedKeyVaultName -Description "$($testPlan.Stage) Key Vault expectation is incorrect"
        Assert-Equal -Expected '$(DeploymentEnvironmentName)' -Actual $parameters.expectedSqlServerName -Description "$($testPlan.Stage) SQL server expectation is incorrect"
        Assert-Equal -Expected $testPlan.Database -Actual $parameters.expectedSqlDatabaseName -Description "$($testPlan.Stage) SQL database expectation is incorrect"
        Assert-Equal -Expected '$(SqlVNextElasticPoolName)' -Actual $parameters.expectedSqlElasticPoolName -Description "$($testPlan.Stage) SQL pool expectation is incorrect"
    }
}

$aggregateDependencies = @(Get-Stage -Pipeline $mainPipeline -Name aggregateCoverage).dependsOn
$tagDependencies = @(Get-Stage -Pipeline $mainPipeline -Name DockerAddTag).dependsOn
$scaleDependencies = @(Get-Stage -Pipeline $mainPipeline -Name scaleDownContainerApps).dependsOn
foreach ($canary in @('testStu3SqlVNext', 'testR4SqlVNext')) {
    Assert-True -Condition ($canary -notin $aggregateDependencies) -Description "$canary blocks aggregate coverage"
    Assert-True -Condition ($canary -notin $tagDependencies) -Description "$canary blocks Docker tag promotion"
    Assert-True -Condition ($canary -in $scaleDependencies) -Description "$canary is missing from scale-down dependencies"
}

$provisionTemplate = $yamlDocuments['build/jobs/provision-deploy.yml']
$provisionTask = @($provisionTemplate.jobs[0].steps | Where-Object { $_.name -eq 'SetAcaOutputs' })[0]
Assert-Equal -Expected '${{ parameters.sqlDatabaseName }}' -Actual (Get-ScriptArgument -Arguments $provisionTask.inputs.ScriptArguments -Name SqlDatabaseName) -Description 'SQL database parameter is not forwarded'
Assert-Equal -Expected '${{ parameters.fhirSdkProviderDefault }}' -Actual (Get-ScriptArgument -Arguments $provisionTask.inputs.ScriptArguments -Name FhirSdkProviderDefault) -Description 'Provider parameter is not forwarded'

$runSqlTemplate = $yamlDocuments['build/jobs/run-sql-tests.yml']
$integrationGateName = '${{ if eq(parameters.runIntegrationTests, true) }}'
$integrationGates = @($runSqlTemplate.jobs | Where-Object { $_.PSObject.Properties[$integrationGateName] })
Assert-Equal -Expected 1 -Actual $integrationGates.Count -Description 'SQL integration tests are not compile-time gated'
$integrationJob = $integrationGates[0].PSObject.Properties[$integrationGateName].Value[0]
Assert-Equal -Expected 'SqlIntegrationTests' -Actual $integrationJob.job -Description 'Compile-time integration gate contains the wrong job'
Assert-True -Condition (-not $integrationJob.PSObject.Properties['condition']) -Description 'SQL integration job still uses runtime gating'
$e2eInvocations = @(Find-TemplateInvocation -Node $runSqlTemplate.jobs -Template 'e2e-tests.yml')
Assert-Equal -Expected 3 -Actual $e2eInvocations.Count -Description 'SQL E2E expectation propagation count changed'
Assert-True -Condition ('${{ parameters.mainCategoryFilter }}' -in @($e2eInvocations.parameters.categoryFilter)) -Description 'Main SQL E2E invocation is missing'
Assert-True -Condition ('Category=IndexAndReindex' -in @($e2eInvocations.parameters.categoryFilter)) -Description 'Reindex SQL E2E invocation is missing'
foreach ($invocation in $e2eInvocations) {
    Assert-Equal -Expected '${{ parameters.expectedFhirSdkProviderDefault }}' -Actual $invocation.parameters.expectedFhirSdkProviderDefault -Description 'run-sql-tests provider expectation is not forwarded'
    Assert-Equal -Expected '${{ parameters.expectedKeyVaultName }}' -Actual $invocation.parameters.expectedKeyVaultName -Description 'run-sql-tests Key Vault expectation is not forwarded'
    Assert-Equal -Expected '${{ parameters.expectedSqlServerName }}' -Actual $invocation.parameters.expectedSqlServerName -Description 'run-sql-tests SQL server expectation is not forwarded'
    Assert-Equal -Expected '${{ parameters.expectedSqlDatabaseName }}' -Actual $invocation.parameters.expectedSqlDatabaseName -Description 'run-sql-tests SQL database expectation is not forwarded'
    Assert-Equal -Expected '${{ parameters.expectedSqlElasticPoolName }}' -Actual $invocation.parameters.expectedSqlElasticPoolName -Description 'run-sql-tests SQL pool expectation is not forwarded'
}

$e2eTemplate = $yamlDocuments['build/jobs/e2e-tests.yml']
$variableInvocation = @($e2eTemplate.steps | Where-Object { $_.template -eq '../tasks/e2e-set-variables.yml' })[0]
Assert-Equal -Expected '../tasks/e2e-set-variables.yml' -Actual $e2eTemplate.steps[1].template -Description 'E2E variable and topology validation no longer runs before the test command'
Assert-Equal -Expected '${{ parameters.expectedFhirSdkProviderDefault }}' -Actual $variableInvocation.parameters.expectedFhirSdkProviderDefault -Description 'e2e-tests provider expectation is not forwarded'
Assert-Equal -Expected '${{ parameters.expectedKeyVaultName }}' -Actual $variableInvocation.parameters.expectedKeyVaultName -Description 'e2e-tests Key Vault expectation is not forwarded'
Assert-Equal -Expected '${{ parameters.expectedSqlServerName }}' -Actual $variableInvocation.parameters.expectedSqlServerName -Description 'e2e-tests SQL server expectation is not forwarded'
Assert-Equal -Expected '${{ parameters.expectedSqlDatabaseName }}' -Actual $variableInvocation.parameters.expectedSqlDatabaseName -Description 'e2e-tests SQL database expectation is not forwarded'
Assert-Equal -Expected '${{ parameters.expectedSqlElasticPoolName }}' -Actual $variableInvocation.parameters.expectedSqlElasticPoolName -Description 'e2e-tests SQL pool expectation is not forwarded'
$providerParameter = @($yamlDocuments['build/tasks/e2e-set-variables.yml'].parameters | Where-Object { $_.name -eq 'expectedFhirSdkProviderDefault' })
Assert-Equal -Expected 1 -Actual $providerParameter.Count -Description 'e2e-set-variables does not declare the provider expectation'
foreach ($parameterName in @('expectedKeyVaultName', 'expectedSqlServerName', 'expectedSqlDatabaseName', 'expectedSqlElasticPoolName')) {
    foreach ($templatePath in @('build/jobs/run-sql-tests.yml', 'build/jobs/e2e-tests.yml', 'build/tasks/e2e-set-variables.yml')) {
        $topologyParameter = @($yamlDocuments[$templatePath].parameters | Where-Object { $_.name -eq $parameterName })
        Assert-Equal -Expected 1 -Actual $topologyParameter.Count -Description "$templatePath does not declare '$parameterName'"
        Assert-Equal -Expected '' -Actual $topologyParameter[0].default -Description "$templatePath '$parameterName' default changes legacy lanes"
    }
}
$setVariablesTask = $yamlDocuments['build/tasks/e2e-set-variables.yml'].steps[0]
$inlineTokens = $null
$inlineParseErrors = $null
$normalizedInline = [regex]::Replace($setVariablesTask.inputs.Inline, '\$\{\{.*?\}\}', 'TemplateValue')
$inlineAst = [System.Management.Automation.Language.Parser]::ParseInput($normalizedInline, [ref]$inlineTokens, [ref]$inlineParseErrors)
Assert-Equal -Expected 0 -Actual $inlineParseErrors.Count -Description 'e2e-set-variables inline PowerShell has parse errors'
$secretVariableFunction = @($inlineAst.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq 'Set-SecretsAsPipelineVariables'
}, $true))
Assert-Equal -Expected 1 -Actual $secretVariableFunction.Count -Description 'Secret pipeline-variable helper is missing'
$secretLoggingCommands = @($secretVariableFunction[0].Body.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.CommandAst] -and
        $node.GetCommandName() -eq 'Write-Host' -and
        $node.Extent.Text.Contains('##vso[task.setvariable')
}, $true))
Assert-Equal -Expected 1 -Actual $secretLoggingCommands.Count -Description 'Secret pipeline-variable command shape changed'
Assert-True `
    -Condition ($secretLoggingCommands[0].Extent.Text.Contains(';issecret=true]')) `
    -Description 'Key Vault values are not emitted as secret Azure Pipelines variables'
$providerAssertionCommands = @($inlineAst.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.CommandAst] -and
        $node.GetCommandName() -eq 'Assert-EffectiveFhirSdkProvider'
}, $true))
Assert-Equal -Expected 1 -Actual $providerAssertionCommands.Count -Description 'e2e-set-variables does not invoke effective-provider validation'
$topologyAssertionCommands = @($inlineAst.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.CommandAst] -and
        $node.GetCommandName() -eq 'Assert-AcaSqlTopology'
}, $true))
Assert-Equal -Expected 1 -Actual $topologyAssertionCommands.Count -Description 'e2e-set-variables does not invoke SQL topology validation'

$provisionScriptPath = Join-Path $repositoryRoot 'build/jobs/scripts/Provision-AcaDeploy.ps1'
$tokens = $null
$parseErrors = $null
$provisionAst = [System.Management.Automation.Language.Parser]::ParseFile($provisionScriptPath, [ref]$tokens, [ref]$parseErrors)
Assert-Equal -Expected 0 -Actual $parseErrors.Count -Description 'Provision-AcaDeploy.ps1 has parse errors'
$assignments = @($provisionAst.FindAll({ param($node) $node -is [System.Management.Automation.Language.AssignmentStatementAst] }, $true))
$databaseAssignment = @($assignments | Where-Object { $_.Left.Extent.Text -eq '$templateParameters["sqlDatabaseName"]' })
Assert-Equal -Expected 1 -Actual $databaseAssignment.Count -Description 'Provision script does not assign the resolved database template parameter'
Assert-Equal -Expected '$sqlDatabaseName' -Actual $databaseAssignment[0].Right.Extent.Text -Description 'Provision script database template parameter uses the wrong value'
$providerAssignment = @($assignments | Where-Object { $_.Left.Extent.Text -eq '$templateParameters["fhirSdkProviderDefault"]' })
Assert-Equal -Expected 1 -Actual $providerAssignment.Count -Description 'Provision script does not assign the resolved provider template parameter'
Assert-Equal -Expected '$sqlDeploymentPlan.FhirSdkProviderDefault' -Actual $providerAssignment[0].Right.Extent.Text -Description 'Provision script provider template parameter bypasses the deployment plan'

$bicepPath = Join-Path $repositoryRoot 'samples/templates/aca/fhir-sql.bicep'
$armJson = & az bicep build --file $bicepPath --stdout
if ($LASTEXITCODE -ne 0) {
    throw 'Bicep compilation failed.'
}

$arm = $armJson | ConvertFrom-Json -Depth 100
Assert-Equal -Expected '' -Actual $arm.parameters.fhirSdkProviderDefault.defaultValue -Description 'Legacy ARM provider default changed'
Assert-True -Condition ($arm.parameters.sqlDatabaseName.defaultValue -like "*parameters('fhirVersion')*") -Description 'ARM database default is not version-specific'
$secret = @($arm.resources | Where-Object { $_.type -eq 'Microsoft.KeyVault/vaults/secrets' })[0]
Assert-True -Condition ($secret.properties.value -like "*parameters('sqlDatabaseName')*") -Description 'Compiled SQL connection string does not use sqlDatabaseName'
Assert-True -Condition ($arm.variables.sdkProviderEnvVars -like "*FhirServer__CoreFeatures__FhirSdkProvider__Default*") -Description 'Compiled ARM does not emit the provider environment variable'
Assert-True -Condition ($arm.variables.sdkProviderEnvVars -like "*empty(parameters('fhirSdkProviderDefault'))*") -Description 'Compiled ARM changes the legacy environment'

Write-Host 'SQL vNext deployment plan validation passed.'
