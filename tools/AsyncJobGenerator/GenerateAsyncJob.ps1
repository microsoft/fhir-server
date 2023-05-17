 <#
.SYNOPSIS
Create changes needed to start development for a new async operation
.DESCRIPTION
Creates and updates files needed to add a new async operation to the FHIR server. These changes will not compile on their own, it is designed to serve as a starting point for developers.
.EXAMPLE
New-FhirServerApiApplicationRegistration -FhirServiceName "myfhiservice" -AppRoles globalReader,globalExporter
.PARAMETER OperationName
Name of the new operation. Words should be seperated by spaces. 
.PARAMETER System
Whether the operation operates at the system level.
.PARAMETER ResourceType
Whether the operation operates at the resource type level.
.PARAMETER ResourceTypeAndId
Whether the operation operates at the resource level.
#>
param(
    [Parameter(Mandatory = $true )]
    [ValidateNotNullOrEmpty()]
    [string]$OperationName,

    [Parameter(Mandatory = $false)]
    [switch]$System,

    [Parameter(Mandatory = $false)]
    [switch]$ResourceType,

    [Parameter(Mandatory = $false)]
    [switch]$ResourceTypeAndId
)

function FillTemplate($TemplatePath, $OutputPath, $TemplateVariables)
{
    $templateContent = Get-ChildItem $TemplatePath | Get-Content
    $outputContent = @()

    for ($i = 0; $i -lt $templateContent.Length; $i++)
    {
        $line = $templateContent[$i]
        foreach ($key in $TemplateVariables.Keys)
        {
            $line = $line.Replace("<$key>", $TemplateVariables[$key])
        }
        $outputContent += $line
    }

    $outputContent | Out-File $OutputPath
}

function AppendToEnd($FilePath, $Content)
{
    $fileContent = Get-ChildItem $FilePath | Get-Content
    $inserted = $false
    $newFileContent = @()
    for ($i = 0; $i -lt $fileContent.Length; $i++)
    {
        if (($inserted -eq $false) -and ($fileContent[$i] -eq "    }"))
        {
            for ($k = 0; $k -lt $Content.Length; $k++)
            {
                $newFileContent += $Content[$k]
            }
            $inserted = $true
        }

        $newFileContent += $fileContent[$i]
    }

    $newFileContent | Out-File $FilePath
}

$jobNamePascalCase = ($textInfo.ToTitleCase($OperationName)).Replace(" ","");
$jobNameSnakeCase = $OperationName.ToLower().Replace(" ","-");

$mediatorActions = @("Create","Cancel","Get")
$jobTypes = @("Processing","Orchestrator")

$templateVariables = @{JobName = $jobNamePascalCase; Action = $mediatorActions[0]; JobType = $jobTypes[0]}

$templateBasePath = ".\tools\AsyncJobGenerator\Templates"

# Controller
FillTemplate -TemplatePath "$templateBasePath\Controller.template" -OutputPath ".\src\Microsoft.Health.Fhir.Shared.Api\Controllers\$($jobNamePascalCase)Controller.cs" -TemplateVariables $templateVariables
AppendToEnd -FilePath ".\src\Microsoft.Health.Fhir.ValueSets\AuditEventSubType.cs" -Content @("","        public const string $jobNamePascalCase = `"$jobNameSnakeCase`";")
AppendToEnd -FilePath ".\src\Microsoft.Health.Fhir.Api\Features\Routing\RouteNames.cs" -Content @("","        public const string Get$($jobNamePascalCase)StatusById = `"Get$($jobNamePascalCase)StatusById`";","","        public const string Cancel$($jobNamePascalCase) = `"Cancel$($jobNamePascalCase)`";")

$operationConstantsContent = @()
$knownRoutesContent = @("","        public const string $jobNamePascalCase = `"`$$jobNameSnakeCase`";")

if ($System -eq $true)
{
    $operationConstantsContent += ""
    $operationConstantsContent += "        public const string $jobNamePascalCase = `"$jobNameSnakeCase`";"
}

if ($ResourceType -eq $true)
{
    $operationConstantsContent += ""
    $operationConstantsContent += "        public const string ResourceType$jobNamePascalCase = `"resource-type-$jobNameSnakeCase`";"

    $knownRoutesContent += "        public const string $($jobNamePascalCase)ResourceType = ResourceType + `"/`" + $jobNamePascalCase;"
}

if ($ResourceTypeAndId -eq $true)
{
    $operationConstantsContent += ""
    $operationConstantsContent += "        public const string ResourceTypeById$jobNamePascalCase = `"resource-type-$jobNameSnakeCase`";"

    $knownRoutesContent += "        public const string $($jobNamePascalCase)ResourceTypeById = ResourceTypeById + `"/`" + $jobNamePascalCase;"
}

if ($System -eq $true)
{
    $knownRoutesContent += "        public const string $($jobNamePascalCase)OperationDefinition = OperationDefinition + `"/`" + OperationsConstants.$jobNamePascalCase;"
}

if ($ResourceType -eq $true)
{
    $knownRoutesContent += "        public const string ResourceType$($jobNamePascalCase)OperationDefinition = OperationDefinition + `"/`" + OperationsConstants.ResourceType$jobNamePascalCase;"
}

if ($ResourceTypeAndId -eq $true)
{
    $knownRoutesContent += "        public const string ResourceTypeById$($jobNamePascalCase)OperationDefinition = OperationDefinition + `"/`" + OperationsConstants.ResourceTypeById$jobNamePascalCase;"
}

AppendToEnd -FilePath ".\src\Microsoft.Health.Fhir.Core\Features\Operations\OperationsConstants.cs" -Content $operationConstantsContent
AppendToEnd -FilePath ".\src\Microsoft.Health.Fhir.Api\Features\Routing\KnownRoutes.cs" -Content $knownRoutesContent

# Mediator
$mediatorFilePathBase = ".\src\Microsoft.Health.Fhir.Core\Features\Operations\$jobNamePascalCase\Mediator"
New-Item -ItemType Directory -Force -Path $mediatorFilePathBase
FillTemplate -TemplatePath "$templateBasePath\MediatorExtensions.template" -OutputPath ".\src\Microsoft.Health.Fhir.Core\Extensions\$($jobNamePascalCase)MediatorExtensions.cs" -TemplateVariables $templateVariables
foreach ($action in $mediatorActions)
{
    $templateVariables["Action"] = $action
    FillTemplate -TemplatePath "$templateBasePath\MediatorHandler.template" -OutputPath "$mediatorFilePathBase\$action$($jobNamePascalCase)Handler.cs" -TemplateVariables $templateVariables
    FillTemplate -TemplatePath "$templateBasePath\MediatorResponse.template" -OutputPath "$mediatorFilePathBase\$action$($jobNamePascalCase)Response.cs" -TemplateVariables $templateVariables

    if ($action -eq "Create")
    {
        FillTemplate -TemplatePath "$templateBasePath\MediatorCreateRequest.template" -OutputPath "$mediatorFilePathBase\$action$($jobNamePascalCase)Request.cs" -TemplateVariables $templateVariables
    }
    else
    {
        FillTemplate -TemplatePath "$templateBasePath\MediatorRequest.template" -OutputPath "$mediatorFilePathBase\$action$($jobNamePascalCase)Request.cs" -TemplateVariables $templateVariables
    }
}

# Background Job
foreach ($jobType in $jobTypes)
{
    $templateVariables["JobType"] = $jobType
    FillTemplate -TemplatePath "$templateBasePath\Job.template" -OutputPath ".\src\Microsoft.Health.Fhir.Core\Features\Operations\$jobNamePascalCase\$($jobNamePascalCase)$($jobType)Job.cs" -TemplateVariables $templateVariables
    AppendToEnd -FilePath ".\src\Microsoft.Health.Fhir.Core\Features\Operations\JobType.cs" -Content @("        $jobNamePascalCase$jobType = ,")
}
AppendToEnd -FilePath ".\src\Microsoft.Health.Fhir.Core\Features\Operations\QueueType.cs" -Content @("        $jobNamePascalCase = ,")
FillTemplate -TemplatePath "$templateBasePath\JobDescription.template" -OutputPath ".\src\Microsoft.Health.Fhir.Core\Features\Operations\$jobNamePascalCase\$($jobNamePascalCase)Description.cs" -TemplateVariables $templateVariables
FillTemplate -TemplatePath "$templateBasePath\JobResult.template" -OutputPath ".\src\Microsoft.Health.Fhir.Core\Features\Operations\$jobNamePascalCase\$($jobNamePascalCase)Result.cs" -TemplateVariables $templateVariables


