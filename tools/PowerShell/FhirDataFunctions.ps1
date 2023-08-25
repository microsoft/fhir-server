<#
.SYNOPSIS
A series of functions for loading various types of FHIR data into a service.
These are the recommended functions to use:
 - GenerateFhirGroups: For making large amounts of interconnected data.
 - NewResourceBatch: For making large amounts of data quickly.
#>

# Generates Groups with linked Patients, Observations, and Encounters.
function GenerateFhirGroups($Number, $Endpoint="localhost:44348", $Token="")
{
	for($i = 0; $i -lt $Number; $i++)
	{
		NewGroup -Endpoint $Endpoint $Token | Out-Null
		if($i%10 -eq 0)
		{
			Write-Host $i
		}
	}
}

# Generates a Group with linked Patients, Observations, and Encounters.
function NewGroup($Endpoint="localhost:44348", $Token="")
{
	$headers = New-Object "System.Collections.Generic.Dictionary[[String],[String]]"
	$headers.Add("Content-Type", "application/json")
	$headers.Add("Authorization", "Bearer ${Token}")

	$entityString = ""
	$numPatients = Get-Random -Minimum 1 -Maximum 5
	for($i = 0; $i -lt $numPatients; $i++)
	{
		$id = NewPatient -Endpoint $Endpoint $Token
		$entityString = "${entityString}{`"entity`":{`"reference`":`"Patient/${id}`"}},"
	}
	$body = "{`"resourceType`":`"Group`",`"type`":`"person`",`"actual`":false,`"member`":[${entityString}]}"
	
	$response = Invoke-RestMethod "https://${Endpoint}/Group" -Method 'POST' -Headers $headers -Body $body
	$response | ConvertTo-Json
}

# Generates a Patient with linked Observations and Encounters
function NewPatient($Endpoint="localhost:44348", $Token="")
{
	$headers = New-Object "System.Collections.Generic.Dictionary[[String],[String]]"
	$headers.Add("Content-Type", "application/json")
	$headers.Add("Authorization", "Bearer ${Token}")

	$name = -join ((65..90) + (97..122) | Get-Random -Count 10 | % {[char]$_})
	$text = -join (,65 * 1000 | % {[char]$_})
	$body = "{`"resourceType`":`"Patient`",`"name`":[{`"family`":`"${name}`"}],`"text`":{`"status`":`"generated`",`"div`":`"<div>${text}</div>`"}}"

	$response = Invoke-RestMethod "https://${Endpoint}/Patient" -Method 'POST' -Headers $headers -Body $body
	
	$numObservations = Get-Random -Minimum 1 -Maximum 5
	for($i = 0; $i -lt $numObservations; $i++)
	{
		NewObservation -PatientId $response.id -Endpoint $Endpoint $Token
	}
	
	$numEncounter = Get-Random -Minimum 1 -Maximum 5
	for($i = 0; $i -lt $numEncounter; $i++)
	{
		NewEncounter -PatientId $response.id -Endpoint $Endpoint $Token
	}
	
	return $response.id
}

# Generates an Observation
function NewObservation($PatientId, $Endpoint="localhost:44348", $Token="")
{
	$headers = New-Object "System.Collections.Generic.Dictionary[[String],[String]]"
	$headers.Add("Content-Type", "application/json")
	$headers.Add("Authorization", "Bearer ${Token}")

	$text = -join (,65 * 1000 | % {[char]$_})
	$body = "{`"resourceType`":`"Observation`",`"status`":`"registered`",`"code`":{`"coding`":[{`"system`":`"system`",`"code`":`"code`"}]},`"subject`":{`"reference`":`"Patient/${PatientId}`"},`"text`":{`"status`":`"generated`",`"div`":`"<div>${text}</div>`"}}"
	
	$response = Invoke-RestMethod "https://${Endpoint}/Observation" -Method 'POST' -Headers $headers -Body $body
}

# Generates an Encounter
function NewEncounter($PatientId, $Endpoint="localhost:44348", $Token="")
{
	$headers = New-Object "System.Collections.Generic.Dictionary[[String],[String]]"
	$headers.Add("Content-Type", "application/json")
	$headers.Add("Authorization", "Bearer ${Token}")

	$text = -join (,65 * 1000 | % {[char]$_})
	$body = "{`"resourceType`":`"Encounter`",`"status`":`"arrived`",`"class`":{`"system`":`"system`",`"code`":`"code`"},`"subject`":{`"reference`":`"Patient/${PatientId}`"},`"text`":{`"status`":`"generated`",`"div`":`"<div>${text}</div>`"}`n}"

	$response = Invoke-RestMethod "https://${Endpoint}/Encounter" -Method 'POST' -Headers $headers -Body $body
}

# Generates a Batch request with various simple FHIR resources.
function NewResourceBatch($Number, $PerBatch=400, $BodySize=1000, $Endpoint="localhost:44348", $Token="")
{
	$headers = New-Object "System.Collections.Generic.Dictionary[[String],[String]]"
	$headers.Add("Content-Type", "application/json")
	$headers.Add("Authorization", "Bearer ${Token}")
	
	$body = "{`"resourceType`": `"Bundle`",`"type`": `"batch`",`"entry`": ["

	$text = -join (,65 * $BodySize | % {[char]$_})
	
	for($i = 0; $i -lt $PerBatch; $i++)
	{
		$resourceName = "Practitioner", "Specimen", "Device" | Get-Random
		$resource = "{`"resource`":{`"resourceType`":`"${resourceName}`",`"text`":{`"status`":`"generated`",`"div`":`"<div>${text}</div>`"}},`"request`":{`"method`":`"POST`",`"url`":`"${resourceName}`"}},"
		$body = "${body}${resource}";
	}
	
	$body = "${body}]}"

	for($i = 0; $i -lt $Number; $i++)
	{
		Invoke-RestMethod "https://${Endpoint}" -Method 'POST' -Headers $headers -Body $body | Out-Null
		if($i%10 -eq 0)
		{
			Write-Host $i
		}
	}
}

# Generates a Batch request with Patients.
function NewPatientBatch($Number, $PerBatch=400, $BodySize=1000, $Endpoint="localhost:44348", $Token="")
{
	$headers = New-Object "System.Collections.Generic.Dictionary[[String],[String]]"
	$headers.Add("Content-Type", "application/json")
	$headers.Add("Authorization", "Bearer ${Token}")
	
	$body = "{`"resourceType`": `"Bundle`",`"type`": `"batch`",`"entry`": ["
	
	Write-Host "Generating body"
	$text = -join (,65 * $BodySize | % {[char]$_})
	
	for($i = 0; $i -lt $PerBatch; $i++)
	{
		$name = -join ((65..90) + (97..122) | Get-Random -Count 10 | % {[char]$_})
		$resource = "{`"resource`":{`"resourceType`":`"Patient`",`"name`":[{`"family`":`"${name}`"}],`"text`":{`"status`":`"generated`",`"div`":`"<div>${text}</div>`"}},`"request`":{`"method`":`"POST`",`"url`":`"Patient`"}},"
		$body = "${body}${resource}";
	}
	
	$body = "${body}]}"
	
	Write-Host "Posting resources"
	for($i = 0; $i -lt $Number; $i++)
	{
		Invoke-RestMethod "https://${Endpoint}" -Method 'POST' -Headers $headers -Body $body | Out-Null
		if($i%10 -eq 0)
		{
			Write-Host $i
		}
	}
}

