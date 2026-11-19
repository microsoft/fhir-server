// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Health.Internal.Fhir.IncludePerf.DataGenerator;

int workerCount = Math.Max(1, Environment.ProcessorCount);
string outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "perf-data");
string profileName = "large";
int patientOverride = -1;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i].ToLowerInvariant())
    {
        case "--profile":
            profileName = args[++i];
            break;
        case "--patients":
            patientOverride = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--output":
            outputDirectory = args[++i];
            break;
        case "--workers":
            workerCount = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--help":
        case "-h":
            Console.WriteLine("Usage: FhirPerfDataGenerator [--profile small|medium|large] [--patients N] [--output DIR] [--workers N]");
            return 0;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            return 1;
    }
}

DatasetProfile profile = DatasetProfile.ForName(profileName);
if (patientOverride > 0)
{
    profile = new DatasetProfile
    {
        Name = profile.Name,
        PatientCount = patientOverride,
        HeavyPatientCount = Math.Min(profile.HeavyPatientCount, patientOverride),
        HeavyPatientMultiplier = profile.HeavyPatientMultiplier,
        PractitionerCount = profile.PractitionerCount,
        OrganizationCount = profile.OrganizationCount,
        LocationCount = profile.LocationCount,
        MedicationCount = profile.MedicationCount,
    };
}

Directory.CreateDirectory(outputDirectory);

Console.WriteLine($"Profile          : {profile.Name}");
Console.WriteLine($"Patients         : {profile.PatientCount:N0} ({profile.HeavyPatientCount} heavy x{profile.HeavyPatientMultiplier})");
Console.WriteLine($"Est. resources   : {profile.EstimateResourceCount():N0}");
Console.WriteLine($"Workers          : {workerCount}");
Console.WriteLine($"Output           : {outputDirectory}");
Console.WriteLine();

var stopwatch = Stopwatch.StartNew();
long patientsDone = 0;
var perTypeCounts = new Dictionary<string, long>(StringComparer.Ordinal);
var perTypeFiles = new Dictionary<string, List<string>>(StringComparer.Ordinal);
var mergeLock = new object();

Parallel.For(
    0,
    workerCount,
    new ParallelOptions { MaxDegreeOfParallelism = workerCount },
    workerId =>
    {
        using var writers = new WriterSet(outputDirectory, workerId, profile.MaxLinesPerFile);
        var generator = new CompartmentGenerator(profile, writers);

        // Worker 0 also emits the shared/universal resources.
        if (workerId == 0)
        {
            generator.WriteSharedResources();
        }

        for (int patientIndex = workerId; patientIndex < profile.PatientCount; patientIndex += workerCount)
        {
            generator.WritePatientCompartment(patientIndex);

            long done = Interlocked.Increment(ref patientsDone);
            if (done % 1000 == 0)
            {
                Console.WriteLine($"  {done:N0}/{profile.PatientCount:N0} patients  [{stopwatch.Elapsed:hh\\:mm\\:ss}]");
            }
        }

        writers.Dispose();

        lock (mergeLock)
        {
            foreach (ShardedNdjsonWriter writer in writers.All)
            {
                if (writer.LineCount == 0)
                {
                    continue;
                }

                perTypeCounts.TryGetValue(writer.ResourceType, out long existing);
                perTypeCounts[writer.ResourceType] = existing + writer.LineCount;

                if (!perTypeFiles.TryGetValue(writer.ResourceType, out List<string> files))
                {
                    files = new List<string>();
                    perTypeFiles[writer.ResourceType] = files;
                }

                files.AddRange(writer.Files.Select(Path.GetFileName));
            }
        }
    });

// Remove any zero-length shard files left behind by workers that produced nothing for a type.
foreach (string file in Directory.EnumerateFiles(outputDirectory, "*.ndjson"))
{
    if (new FileInfo(file).Length == 0)
    {
        File.Delete(file);
    }
}

long totalResources = perTypeCounts.Values.Sum();
long totalBytes = Directory.EnumerateFiles(outputDirectory, "*.ndjson").Sum(f => new FileInfo(f).Length);

var manifest = new
{
    profile = profile.Name,
    generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
    patientCount = profile.PatientCount,
    heavyPatientCount = profile.HeavyPatientCount,
    heavyPatientMultiplier = profile.HeavyPatientMultiplier,
    totalResources,
    totalBytes,

    // Patient ids the benchmark should target. Heavy patients are the worst case for include fan-out;
    // a mid-range patient represents the typical case.
    heavyPatientIds = Enumerable.Range(0, Math.Min(profile.HeavyPatientCount, 5))
        .Select(CompartmentGenerator.PatientId).ToArray(),
    typicalPatientIds = new[]
    {
        CompartmentGenerator.PatientId(profile.PatientCount / 2),
        CompartmentGenerator.PatientId((profile.PatientCount / 2) + 1),
    },

    resourceTypes = perTypeCounts
        .OrderByDescending(kvp => kvp.Value)
        .Select(kvp => new
        {
            type = kvp.Key,
            count = kvp.Value,
            files = perTypeFiles[kvp.Key].OrderBy(f => f, StringComparer.Ordinal).ToArray(),
        })
        .ToArray(),
};

string manifestPath = Path.Combine(outputDirectory, "manifest.json");
File.WriteAllText(
    manifestPath,
    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

stopwatch.Stop();

Console.WriteLine();
Console.WriteLine($"Generated {totalResources:N0} resources ({totalBytes / 1024.0 / 1024.0:N0} MB) in {stopwatch.Elapsed:hh\\:mm\\:ss}");
Console.WriteLine();
foreach (var kvp in perTypeCounts.OrderByDescending(k => k.Value))
{
    Console.WriteLine($"  {kvp.Key,-20} {kvp.Value,12:N0}  ({perTypeFiles[kvp.Key].Count} files)");
}

Console.WriteLine();
Console.WriteLine($"Manifest: {manifestPath}");

return 0;
