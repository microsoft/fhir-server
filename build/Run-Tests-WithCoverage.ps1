## Suggested workaround for merging coverage results: https://github.com/coverlet-coverage/coverlet/pull/225#issuecomment-573896446
$testResultsDirectory="$Env:AGENT_BUILDDIRECTORY/TestResults"
$coverageJsonFullPath = "$testResultsDirectory/coverage.json"
$coverageCoberturaFullPath = "$testResultsDirectory/coverage.cobertura.xml"
$buildConfiguration = "Release"

Write-Host "Test directory: $testResultsDirectory"

# calculate code coverage
Get-ChildItem -Recurse -Filter *UnitTests*.csproj | 
Foreach-Object {
  $dir = "$testResultsDirectory/$($_.BaseName)"
  dotnet test $_.FullName --configuration $buildConfiguration --no-build --logger trx --results-directory "$dir" --collect:"XPlat Code Coverage" -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format="json,cobertura" -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Include="[Microsoft.Health.Fhir.*]*" -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Exclude="[*Tests*]*,[*.Web]*,[*.ValueSets]*,[*.SqlServer]*.Schema.Model.*" -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.MergeWith="$coverageJsonFullPath"

  # by copying back the current set of coverage results and using MergeWith, the results will be aggregated
  if (Test-Path -Path $dir){
    Copy-Item -Path "$dir/*/coverage.json" -Destination $coverageJsonFullPath -Force
    Copy-Item -Path "$dir/*/coverage.cobertura.xml" -Destination $coverageCoberturaFullPath -Force
  }
}
