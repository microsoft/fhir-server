// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace FHIRDataSynth
{
    internal static class RDUtility
    {
        public static string ResourcesExtension { get; } = ".ndjson";

        public static async Task DownloadBlobGroupAsync(string connectionString, string containerName, string blobGroupDir, string localPath)
        {
            try
            {
                if (Directory.Exists(localPath + blobGroupDir))
                {
                    // Safety check, so we don't start overwriting if we already downloaded.
                    throw new FHIRDataSynthException($"Directory '{localPath + blobGroupDir}' already exists!");
                }

                Directory.CreateDirectory(localPath + blobGroupDir);
                BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
                BlobContainerClient blobContainerClient = blobServiceClient.GetBlobContainerClient(containerName);
                await foreach (BlobItem blobItem in blobContainerClient.GetBlobsAsync(prefix: blobGroupDir))
                {
                    BlobClient blobClient = blobContainerClient.GetBlobClient(blobItem.Name);
                    Response response = await blobClient.DownloadToAsync(localPath + blobItem.Name);
                    if (response.Status < 200 || response.Status > 299)
                    {
                        throw new FHIRDataSynthException($"Failed downloading blob '{blobItem.Name}'!");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
            }
        }

        public static async Task GetResourceBlobsInfoAsync(
            string connectionString,
            string blobContainerName,
            string resourceBlobsInfoPath,
            string resourceBlobsInfoTotalsPath)
        {
            try
            {
                BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
                BlobContainerClient blobContainerClient = blobServiceClient.GetBlobContainerClient(blobContainerName);
                Dictionary<string, long> blobContentLenghts = new Dictionary<string, long>();
                SortedSet<string> blobGroupDirNames = new SortedSet<string>();
                SortedSet<string> resourceNames = new SortedSet<string>();
                Dictionary<string, long> resourceTotalContantLenghts = new Dictionary<string, long>();
                await foreach (BlobItem blobItem in blobContainerClient.GetBlobsAsync())
                {
                    string extension = Path.GetExtension(blobItem.Name);
                    if (extension != ".ndjson")
                    {
                        continue;
                    }

                    blobContentLenghts.Add(blobItem.Name, blobItem.Properties.ContentLength ?? 0);
                    int delimiter = blobItem.Name.IndexOf('/', StringComparison.Ordinal);
                    if (delimiter < 0)
                    {
                        continue;
                    }

                    string blobGroupDirName = blobItem.Name.Substring(0, delimiter + 1);
                    string resourceName = blobItem.Name.Substring(delimiter + 1);
                    blobGroupDirNames.Add(blobGroupDirName);
                    resourceNames.Add(resourceName);
                }

                using (StreamWriter stream = new StreamWriter(resourceBlobsInfoPath))
                {
                    int groupDirNumber = 1;
                    await stream.WriteAsync("#,Group Dir");
                    foreach (string resourceName in resourceNames)
                    {
                        await stream.WriteAsync($",{Path.GetFileNameWithoutExtension(resourceName)}");
                        resourceTotalContantLenghts[resourceName] = 0;
                    }

                    await stream.WriteLineAsync();
                    foreach (string blobGroupDirName in blobGroupDirNames)
                    {
                        await stream.WriteAsync($"{groupDirNumber++},{blobGroupDirName}");
                        foreach (string resourceName in resourceNames)
                        {
                            long blobContentLength;
                            blobContentLenghts.TryGetValue(blobGroupDirName + resourceName, out blobContentLength);
                            await stream.WriteAsync($",{blobContentLength}");
                            resourceTotalContantLenghts[resourceName] = blobContentLength + resourceTotalContantLenghts[resourceName];
                        }

                        await stream.WriteLineAsync();
                    }

                    long total = 0;
                    foreach (string resourceName in resourceNames)
                    {
                        total += resourceTotalContantLenghts[resourceName];
                    }

                    await stream.WriteAsync($"TOTAL,{total}");
                    foreach (string resourceName in resourceNames)
                    {
                        await stream.WriteAsync($",{resourceTotalContantLenghts[resourceName]}");
                    }

                    await stream.WriteLineAsync();
                }

                using (StreamWriter stream = new StreamWriter(resourceBlobsInfoTotalsPath))
                {
                    await stream.WriteLineAsync(CalculatorTargetRatios.ResourcesTotalSizeHeader);
                    foreach (string resourceName in resourceNames)
                    {
                        await stream.WriteLineAsync($"{Path.GetFileNameWithoutExtension(resourceName)},{resourceTotalContantLenghts[resourceName]}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
            }
        }

        public static void WriteSingleResourceExamples(string localPath, string blobGroupDir, string singleResourceExamplesDir)
        {
            try
            {
                string[] fileEntries = Directory.GetFiles(localPath + blobGroupDir);
                foreach (string fileName in fileEntries)
                {
                    using (StreamReader streamReader = new StreamReader(fileName))
                    {
                        string extension = Path.GetExtension(fileName);
                        if (extension != ".ndjson")
                        {
                            continue;
                        }

                        string firstLine = streamReader.ReadLine();
                        string outputFileName = Path.GetFileNameWithoutExtension(fileName);
                        File.WriteAllText(singleResourceExamplesDir + outputFileName + ".json", firstLine);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
            }
        }

        private static BlobInfo GetBlobInfo(string resourceName, StreamReader streamReader)
        {
            Console.WriteLine($"### {resourceName} processing start.");
            BlobInfo blobInfo = new BlobInfo();
            int lineNumber = 0;
            string line;
            while ((line = streamReader.ReadLine()) != null)
            {
                if (lineNumber % 100000 == 0)
                {
                    Console.WriteLine($"  {resourceName} line {lineNumber}.");
                }

                lineNumber++;
                blobInfo.LinesLengthSum += line.Length;
                RDResourceJSON json = JsonSerializer.Deserialize<RDResourceJSON>(line);
                if (json.resourceType != resourceName)
                {
                    throw new FHIRDataSynthException($"Expected {resourceName}, found {json.resourceType}!");
                }

                if (json.patient.reference != null)
                {
                    if (json.patient.reference.StartsWith("Patient/", StringComparison.Ordinal))
                    {
                        blobInfo.PatientRefIds.Add(json.patient.reference.Substring("Patient/".Length));
                    }
                    else
                    {
                        blobInfo.PatientsRefsNotPatient++;
                    }
                }

                if (json.subject.reference != null)
                {
                    if (json.subject.reference.StartsWith("Patient/", StringComparison.Ordinal))
                    {
                        blobInfo.PatientRefIds.Add(json.subject.reference.Substring("Patient/".Length));
                    }
                    else
                    {
                        blobInfo.SubjectsRefsNotPatient++;
                    }
                }

                if (!blobInfo.Ids.Add(json.id))
                {
                    blobInfo.DuplicateIds++;
                }
            }

            blobInfo.LinesCount = lineNumber;
            Console.WriteLine($"### {resourceName} processing end.");
            return blobInfo;
        }

        public static void GetBlobGroupInfo(string blobGroupDir, string outputPath)
        {
            if (File.Exists(outputPath))
            {
                // Safety check, so we don't start overwriting if we already calculated.
                throw new FHIRDataSynthException($"File '{outputPath}' already exists!");
            }

            Dictionary<string, BlobInfo> blobInfo = new Dictionary<string, BlobInfo>();
            string[] fileEntries = Directory.GetFiles(blobGroupDir);
            foreach (string fileName in fileEntries)
            {
                if (Path.GetExtension(fileName) != ".ndjson")
                {
                    continue;
                }

                string resourceName = Path.GetFileNameWithoutExtension(fileName);
                using (StreamReader streamReader = new StreamReader(fileName))
                {
                    blobInfo.Add(resourceName, GetBlobInfo(resourceName, streamReader));
                }
            }

            foreach (KeyValuePair<string, BlobInfo> info in blobInfo)
            {
                info.Value.ConsoleWriteLine(info.Key, blobInfo["Patient"].Ids);
            }

            using (StreamWriter streamWriter = new StreamWriter(outputPath))
            {
                streamWriter.WriteLine(CalculatorTargetRatios.OneResourceGroupInfoHeader);
                foreach (KeyValuePair<string, BlobInfo> info in blobInfo)
                {
                    streamWriter.WriteLine(info.Value.Line(info.Key, blobInfo["Patient"].Ids));
                }
            }
        }

        private sealed class BlobInfo
        {
            public HashSet<string> Ids { get; set; } = new HashSet<string>();

            public int DuplicateIds { get; set; }

            public int PatientsRefsNotPatient { get; set; }

            public int SubjectsRefsNotPatient { get; set; }

            public HashSet<string> PatientRefIds { get; set; } = new HashSet<string>();

            public int LinesCount { get; set; }

            public long LinesLengthSum { get; set; }

            public void ConsoleWriteLine(string resourceName, HashSet<string> patientIds)
            {
                Console.WriteLine($"### {resourceName} info:");
                Console.WriteLine($"  ids.Count = {Ids.Count}.");
                Console.WriteLine($"  duplicateIds = {DuplicateIds}.");
                Console.WriteLine($"  patientsRefsNotPatient = {PatientsRefsNotPatient}.");
                Console.WriteLine($"  subjectsRefsNotPatient = {SubjectsRefsNotPatient}.");
                Console.WriteLine($"  patientRefIds.Count = {PatientRefIds.Count}.");
                Console.WriteLine($"  intersect.Count() = {patientIds.Intersect(PatientRefIds).Count()}.");
                Console.WriteLine($"  linesCount = {LinesCount}.");
                Console.WriteLine($"  linesLengthSum = {LinesLengthSum}.");
                Console.WriteLine($"###");
            }

            public string Line(string resourceName, HashSet<string> patientIds)
            {
                return $"{resourceName},{Ids.Count},{DuplicateIds},{PatientsRefsNotPatient},{SubjectsRefsNotPatient},{PatientRefIds.Count},{patientIds.Intersect(PatientRefIds).Count()},{LinesCount},{LinesLengthSum}";
            }
        }

#pragma warning disable CA1812 // Code analyzer does not recognize that class is instantiated by JSON de-serializer.
#pragma warning disable SA1300 // JSON serialization/de-serialization, follow JSON naming convention.
        public sealed class RDResourceJSON
        {
            public string resourceType { get; set; }

            public string id { get; set; }

            public ReferenceJSON patient { get; set; }

            public ReferenceJSON subject { get; set; }

            public struct ReferenceJSON
            {
                public string reference { get; set; }
            }
        }
#pragma warning restore SA1300
#pragma warning restore CA1812
    }
}
