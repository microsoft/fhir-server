# Run the REDox component experiment

This tool compares JSON components using the server's Firely packages.
It does not start a server or connect to SQL.
See the [design](../../docs/experiments/redox-performance-experiment.md) for the
application experiment and its separate correctness and performance gates.

## Build the pinned dependency

Use a new scratch directory for the Apache-2.0 REDox source.
Keep this third-party checkout outside the FHIR Server worktree.
From the FHIR Server repository root, run these commands in PowerShell.

```powershell
$redoxSource = Join-Path $env:TEMP 'redox-experiment-v1'
git clone --branch v1.0.0 --depth 1 https://github.com/CAPCOM-TD-OSS/REDox.git $redoxSource
git -C $redoxSource rev-parse HEAD
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build `
	(Join-Path $redoxSource 'src\REDox\REDox.csproj') -c Release --nologo -v:q
```

The revision must be `47a9b74442e8fc3456163fd8a02db9245a111bed`.
Both dependency and instrument builds must succeed before proceeding.

## Build and check the instrument

```powershell
$redoxAssembly = Join-Path $redoxSource 'src\REDox\bin\Release\net10.0\REDox.dll'
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build `
	tools\RedoxExperiment\RedoxExperiment.csproj -c Release --nologo -v:q `
	"-p:RedoxAssembly=$redoxAssembly" -p:RestoreLockedMode=true
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" `
	tools\RedoxExperiment\bin\Release\net10.0\RedoxExperiment.dll verify `
	src\Microsoft.Health.Fhir.Azure\IntegrationDataStore\TestData\representative-import-1000.ndjson.gz
```

The recorded experiment used the existing package cache when NuGet TLS failed.
To repeat that fallback, add `-p:RestoreSources='C:\.tools\.nuget\packages'`
and `-p:NuGetAudit=false` to the instrument build.
Use those flags only if this cache exists and contains the locked dependencies.
Do not disable TLS verification or change `global.json`.

The checks assert literal FHIR values, decimal scale, and corpus equivalence.
Exit code `3` means that input acceptance differs from the baseline.
The current prototype reports four such differences.
That result stops integration even though the valid-resource assertions pass.
An assertion failure also stops the run.

## Run the fixed screening schedule

Stop other owned builds before measuring.
Use a new output directory for each complete run.
The runner refuses to overwrite an existing directory.

```powershell
& .\tools\RedoxExperiment\Invoke-Experiment.ps1 `
	-RedoxSource $redoxSource `
	-OutputDirectory .\tools\RedoxExperiment\results\local-run
python tools\RedoxExperiment\analyze_results.py --self-test
python tools\RedoxExperiment\analyze_results.py `
	tools\RedoxExperiment\results\local-run |
	Set-Content tools\RedoxExperiment\results\local-run\summary.json -Encoding utf8
```

The runner continues after exit code `3` only to collect diagnostic component
timings on the valid corpus. This does not waive the integration gate.
It runs two baseline-versus-baseline pairs and five baseline-versus-REDox pairs
for each workload, all in fresh sequential processes.
The candidate order is balanced and shuffled with a fixed seed.
Each process warms up for at least three seconds.
`DOTNET_PROCESSOR_COUNT=2` limits the runtime's reported processor count and
server-GC heap count. It is not a CPU affinity or operating-system CPU quota.

The fixed workloads are Firely parsing, Firely parsing plus serialization, and
an isolated metadata rewrite. They run 20, 20, and 1,000 passes respectively over
the embedded 1,000-resource corpus. The analysis rejects incomplete schedules.
Do not choose a different run length after seeing candidate results.

Keep the manifest, verification output, all raw measurements, and summary.
Use the [results report](../../docs/experiments/redox-performance-results.md)
for interpretation and limits. Process memory snapshots are not steady service
memory measurements. Component elapsed times are not API latencies.
