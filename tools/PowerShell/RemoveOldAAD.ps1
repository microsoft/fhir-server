# Checks if the AAD User is suitable to be removed.
function IsCiTestAadUser {
 param([string]$displayName)
 
    if ($displayName.StartsWith("f") -and
        ($displayName.Contains("pr") -and $displayName.Contains("-globalAdminUser")) -or
        ($displayName.Contains("pr") -and $displayName.Contains("-globalConverterUser")) -or
        ($displayName.Contains("pr") -and $displayName.Contains("-globalExporterUser")) -or
        ($displayName.Contains("pr") -and $displayName.Contains("-globalReaderUser")) -or
        ($displayName.Contains("pr") -and $displayName.Contains("-globalImporterUser")) -or
        ($displayName.Contains("pr") -and $displayName.Contains("-globalWriterUser"))) {
        return $true;
    }

    #if ($displayName.StartsWith("msh-fhir-pr-") -or $displayName.StartsWith("msh-fhir-ci2-"))
    #{
    #    return $true;
    #}

    #if (($displayName.StartsWith("dcm-pr") -and $displayName.Contains("User")) -or
    #    ($displayName.StartsWith("dcm-pr") -and $displayName.Contains("user")))
    #{
    #    return $true;
    #}

    return $false;
}

# Checks if the AAD Application is suitable to be removed.
function IsCiTestAadApplication {
 param([string]$displayName)
 
    if ($displayName.Contains("fhir-pr") -or
        #$displayName.Contains("dcm-pr") -or
		#$displayName.Contains(".resoluteopensource.onmicrosoft.com") -or
        ($displayName.Contains("pr") -and $displayName.Contains("-smart-")) -or
        ($displayName.Contains("pr") -and $displayName.Contains("-smartUserClient")) -or
        ($displayName.Contains("pr") -and $displayName.Contains("nativeClient")) -or
        ($displayName.Contains("pr") -and $displayName.Contains("wrongAudienceClient")) -or
        ($displayName.Contains("pr") -and $displayName.Contains("globalAdminServicePrincipal"))) {
        return $true;
    }
    return $false;
}

# Connect-MgGraph -Scopes "Application.ReadWrite.All, User.ReadWrite.All"

$dateLimit = (Get-Date).AddMonths(-1)

##################################################
Write-Host ""
Write-Host "## Deleting AAD Users"

$azureUsers = Get-MgUser -All:$true
$totalCount = $azureUsers.length
$counter = 0

Write-Host "Total users: $totalCount"

foreach ($aadUser in $azureUsers) {

    $displayName = $aadUser.DisplayName
    if (IsCiTestAadUser($displayName)) {
		$objectId = $aadUser.Id
		Write-Host "$counter - User Name: $displayName / Id: $objectId / CreationTime: $creationTime"
		Remove-MgUser -UserId $objectId | Out-Null
		Write-Host "$counter - SOFT REMOVED - User Name: $displayName / Id: $objectId"

		$counter += 1
    }
}

##################################################
Write-Host ""
Write-Host "## Deleting AAD Applications"

$azureApplications = Get-MgApplication -All:$true
$totalCount = $azureApplications.length
$counter = 0

Write-Host "Total apps: $totalCount"

foreach ($app in $azureApplications) {

    $displayName = $app.DisplayName
    if (IsCiTestAadApplication($displayName)) {

        $objectId = $app.Id

        $startDate = $app.PasswordCredentials[0].StartDate
        $endDate = $app.PasswordCredentials[0].EndDate

        if ($startDate -lt $dateLimit) {
            
            Write-Host "$counter - App Name: $displayName / Id: $objectId / StartDate: $startDate"
            Remove-MgApplication -ApplicationId $objectId | Out-Null
            Write-Host "$counter - SOFT REMOVED - App Name: $displayName / Id: $objectId"
			Remove-MgDirectoryDeletedItem -DirectoryObjectId $objectId | Out-Null
			Write-Host "$counter - HARD REMOVED - App Name: $displayName / Id: $objectId"
            $counter += 1
        }
    }
}
