$buildNumber = "$(Release.Artifacts.CIBuild.BuildNumber)"
$build = "$(Build.BuildNumber)"
$timestamp= $build.Substring($build.IndexOf('-')+1)
$versions = "stu3", "r4", "r4b", "r5"
$registry = "healthplatformregistry.azurecr.io"
$repositories = $versions | ForEach-Object { "$_`_fhir-server"; "public/healthcareapis/$_-fhir-server"; }

# Log in to the registry
$userName = "00000000-0000-0000-0000-000000000000"
$password = $(az acr login --name $registry --expose-token --output tsv --query accessToken)
oras login -u $userName -p $password $registry

foreach ($repo in $repositories) {
    # Get all tags from the repository except the latest ones
    $tags = oras repo tags "${registry}/${repo}"
    $tags = $tags.Where({ !$_.Contains($buildNumber) -and !$_.Contains($timestamp) -and !$_.Contains("latest") -and !$_.Contains("release") })

    # Attach the lifecycle metadata to older images
    foreach ($tag in $tags) {
        $image = "${registry}/${repo}:${tag}"
        $artifact = oras discover -o json --artifact-type "application/vnd.microsoft.artifact.lifecycle" $image
        $json = $artifact -join "" | ConvertFrom-Json
        if ($null -eq $json.manifests) {
            Write-Output "'${image}' needs the lifestyle metadata."
            $annotation = "vnd.microsoft.artifact.lifecycle.end-of-life.date=" + [DateTime]::UtcNow.ToString("O")
            oras attach --artifact-type "application/vnd.microsoft.artifact.lifecycle" --annotation $annotation $image
        }
        else {
            Write-Output "'${image}' already has the lifestyle metadata."
        }
    }

    # Attach the lineage metadata to the new image
    $image = "${registry}/${repo}:${buildNumber}"
    $artifact = oras discover -o json --artifact-type "application/vnd.microsoft.artifact.lineage" $image
    $json = $artifact -join "" | ConvertFrom-Json
    if ($null -eq $json.manifests) {
        Write-Output "'${image}' needs the lineage metadata."
        $annotation = "vnd.microsoft.artifact.lineage.rolling-tag=${buildNumber}"
        oras attach --artifact-type "application/vnd.microsoft.artifact.lineage" --annotation $annotation $image
    }
    else {
        Write-Output "'${image}' already has the lineage metadata."
    }
}
