using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FHIRDataSynth;
using ResourceProcessorNamespace;

internal class RDUtility
{
    public static string ResourcesExtension { get; } = ".ndjson";

    public static async Task DownloadBlobGroupAsync(string connectionString, string containerName, string blobGroupDir, string localPath)
    {
        try
        {
            if (Directory.Exists(localPath + blobGroupDir))
            {
                // Safety check, so we don't start overwriting if we already downloaded.
                throw new Exception($"Directory '{localPath + blobGroupDir}' already exists!");
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
                    throw new Exception($"Failed downloading blob '{blobItem.Name}'!");
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
                    throw new Exception("Incorrect blob path!");
                }

                string blobGroupDirName = blobItem.Name.Substring(0, delimiter + 1);
                string resourceName = blobItem.Name.Substring(delimiter + 1);
                blobGroupDirNames.Add(blobGroupDirName);
                resourceNames.Add(resourceName);
            }

            using (StreamWriter stream = new StreamWriter(resourceBlobsInfoPath))
            {
                int groupDirNumber = 1;
                stream.Write("#,Group Dir");
                foreach (string resourceName in resourceNames)
                {
                    stream.Write($",{Path.GetFileNameWithoutExtension(resourceName)}");
                    resourceTotalContantLenghts[resourceName] = 0;
                }

                stream.WriteLine();
                foreach (string blobGroupDirName in blobGroupDirNames)
                {
                    stream.Write($"{groupDirNumber++},{blobGroupDirName}");
                    foreach (string resourceName in resourceNames)
                    {
                        long blobContentLength;
                        blobContentLenghts.TryGetValue(blobGroupDirName + resourceName, out blobContentLength);
                        stream.Write($",{blobContentLength}");
                        resourceTotalContantLenghts[resourceName] = blobContentLength + resourceTotalContantLenghts[resourceName];
                    }

                    stream.WriteLine();
                }

                long total = 0;
                foreach (string resourceName in resourceNames)
                {
                    total += resourceTotalContantLenghts[resourceName];
                }

                stream.Write($"TOTAL,{total}");
                foreach (string resourceName in resourceNames)
                {
                    stream.Write($",{resourceTotalContantLenghts[resourceName]}");
                }

                stream.WriteLine();
            }

            using (StreamWriter stream = new StreamWriter(resourceBlobsInfoTotalsPath))
            {
                stream.WriteLine(CalculatorTargetRatios.ResourcesTotalSizeHeader);
                foreach (string resourceName in resourceNames)
                {
                    stream.WriteLine($"{Path.GetFileNameWithoutExtension(resourceName)},{resourceTotalContantLenghts[resourceName]}");
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

    private class BlobInfo
    {
        public HashSet<string> ids = new HashSet<string>();
        public int duplicateIds = 0;
        public int patientsRefsNotPatient = 0;
        public int subjectsRefsNotPatient = 0;
        public HashSet<string> patientRefIds = new HashSet<string>();
        public int linesCount = 0;
        public long linesLengthSum = 0;

        public void ConsoleWriteLine(string resourceName, HashSet<string> patientIds)
        {
            Console.WriteLine($"### {resourceName} info:");
            Console.WriteLine($"  ids.Count = {ids.Count}.");
            Console.WriteLine($"  duplicateIds = {duplicateIds}.");
            Console.WriteLine($"  patientsRefsNotPatient = {patientsRefsNotPatient}.");
            Console.WriteLine($"  subjectsRefsNotPatient = {subjectsRefsNotPatient}.");
            Console.WriteLine($"  patientRefIds.Count = {patientRefIds.Count}.");
            Console.WriteLine($"  intersect.Count() = {patientIds.Intersect(patientRefIds).Count()}.");
            Console.WriteLine($"  linesCount = {linesCount}.");
            Console.WriteLine($"  linesLengthSum = {linesLengthSum}.");
            Console.WriteLine($"###");
        }

        public string Line(string resourceName, HashSet<string> patientIds)
        {
            return $"{resourceName},{ids.Count},{duplicateIds},{patientsRefsNotPatient},{subjectsRefsNotPatient},{patientRefIds.Count},{patientIds.Intersect(patientRefIds).Count()},{linesCount},{linesLengthSum}";
        }
    }

    public struct ReferenceJSON
    {
        public string reference { get; set; }
    }

    public abstract class RDResourceJSON
    {
        public string resourceType { get; set; }

        public string id { get; set; }

        public ReferenceJSON patient { get; set; }

        public ReferenceJSON subject { get; set; }
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
            blobInfo.linesLengthSum += line.Length;
            RDResourceJSON json = JsonSerializer.Deserialize<RDResourceJSON>(line);
            if (json.resourceType != resourceName)
            {
                throw new Exception($"Expected {resourceName}, found {json.resourceType}!");
            }

            if (json.patient.reference != null)
            {
                if (json.patient.reference.StartsWith("Patient/", StringComparison.Ordinal))
                {
                    blobInfo.patientRefIds.Add(json.patient.reference.Substring("Patient/".Length));
                }
                else
                {
                    blobInfo.patientsRefsNotPatient++;
                }
            }

            if (json.subject.reference != null)
            {
                if (json.subject.reference.StartsWith("Patient/", StringComparison.Ordinal))
                {
                    blobInfo.patientRefIds.Add(json.subject.reference.Substring("Patient/".Length));
                }
                else
                {
                    blobInfo.subjectsRefsNotPatient++;
                }
            }

            if (!blobInfo.ids.Add(json.id))
            {
                blobInfo.duplicateIds++;
            }
        }

        blobInfo.linesCount = lineNumber;
        Console.WriteLine($"### {resourceName} processing end.");
        return blobInfo;
    }

    public static void GetBlobGroupInfo(string localPath, string blobGroupDir, string outputPath)
    {
        try
        {
            if (File.Exists(outputPath))
            {
                // Safety check, so we don't start overwriting if we already calculated.
                throw new Exception($"File '{outputPath}' already exists!");
            }

            Dictionary<string, BlobInfo> blobInfo = new Dictionary<string, BlobInfo>();
            string[] fileEntries = Directory.GetFiles(localPath + blobGroupDir);
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
                info.Value.ConsoleWriteLine(info.Key, blobInfo["Patient"].ids);
            }

            using (StreamWriter streamWriter = new StreamWriter(outputPath))
            {
                streamWriter.WriteLine(CalculatorTargetRatios.OneResourceGroupInfoHeader);
                foreach (KeyValuePair<string, BlobInfo> info in blobInfo)
                {
                    streamWriter.WriteLine(info.Value.Line(info.Key, blobInfo["Patient"].ids));
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
        }
    }
}
