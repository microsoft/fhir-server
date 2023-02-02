// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace FHIRDataSynth
{
    internal sealed class ServerImport
    {
        private static async Task<bool> ImportSingle(Uri uri, string importParametersJsonString, HttpClient client, ImportResult currentResult)
        {
            using (HttpRequestMessage request = new HttpRequestMessage()
            {
                RequestUri = uri,
                Method = HttpMethod.Post,
                Content = new StringContent(importParametersJsonString, Encoding.UTF8, "application/fhir+json"),
            })
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/fhir+json"));
                request.Headers.Add("Prefer", "respond-async");
                using (HttpResponseMessage response = await client.SendAsync(request))
                {
                    currentResult.responseStatusCode = (int)response.StatusCode;
                    currentResult.responseStatusCodeString = response.StatusCode.ToString();
                    currentResult.responseReasonPhrase = response.ReasonPhrase;
                    if (!response.IsSuccessStatusCode)
                    {
                        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                        {
                            return true;
                        }

                        throw new FHIRDataSynthException("Server did not return success code.");
                    }

                    Uri contentLocation = response.Content.Headers.ContentLocation;
                    if (contentLocation == null)
                    {
                        throw new FHIRDataSynthException($"Server did not return Content-Location header.");
                    }

                    if (contentLocation.Segments == null || contentLocation.Segments.Length != 4 || contentLocation.Segments[0] != "/" ||
                        contentLocation.Segments[1] != "_operations/" || contentLocation.Segments[2] != "import/")
                    {
                        throw new FHIRDataSynthException($"Server returned unrecognized Content-Location header {contentLocation}.");
                    }

                    currentResult.importResultUrl = contentLocation.ToString();
                }
            }

            return false;
        }

        private static async Task ImportSingleBlobPerServerCall(string serverUrl, string resourceGroupCountStr, string inputUrl, string inputBlobContainerName, string importResultFileName, string inputConnectionString)
        {
            if (inputUrl.EndsWith('/'))
            {
                throw new FHIRDataSynthException($"Input url {inputUrl} must not end with '/'");
            }

            if (serverUrl.EndsWith('/'))
            {
                throw new FHIRDataSynthException($"Server url {serverUrl} must not end with '/'");
            }

            if (!int.TryParse(resourceGroupCountStr, out int resourceGroupCount))
            {
                throw new FHIRDataSynthException($"Resource group count {resourceGroupCount} is not valid int.");
            }

            if (resourceGroupCount < 1)
            {
                throw new FHIRDataSynthException($"Resource group count {resourceGroupCount} is invalid , must be greater than 0.");
            }

            SortedSet<string> dirs = await BlobResourceProcessor.GetResourceGroupDirsAsync(inputConnectionString, inputBlobContainerName);
            if (dirs.Count < resourceGroupCount)
            {
                throw new FHIRDataSynthException($"Tried to import {resourceGroupCount} resource group(s), but {inputBlobContainerName} contains only {dirs.Count}.");
            }

            Uri uri = new Uri(serverUrl + "/$import");
            if (uri.Scheme != "http")
            {
                Console.WriteLine($"WARNING, server may not be able to accept '{uri.Scheme}' requests, try 'http' if failure.");
            }

            bool success = true;
            BlobServiceClient blobServiceClient = new BlobServiceClient(inputConnectionString);
            BlobContainerClient blobContainerClient = blobServiceClient.GetBlobContainerClient(inputBlobContainerName);
            ImportResultCollection result = new ImportResultCollection();
            result.importResult = new List<ImportResult>();
            using (HttpClient client = new HttpClient())
            {
                foreach (string dir in dirs)
                {
                    if (resourceGroupCount < 1)
                    {
                        break;
                    }

                    resourceGroupCount--;

                    await foreach (BlobHierarchyItem blobHierarchyItem in blobContainerClient.GetBlobsByHierarchyAsync(prefix: dir))
                    {
                        // if (blobHierarchyItem.Blob.Name.IndexOf("Observation.ndjson") == -1) continue;

                        ImportResult currentResult = new ImportResult();
                        result.importResult.Add(currentResult);
                        currentResult.responseSuccess = true;
                        try// Main reason for this try block is to capture SendAsync exceptions.
                        {
                            // Create server call import parameter.
                            ImportParameters importParametersJson = new ImportParameters() { resourceType = "Parameters", parameter = new List<Parameter>() };
                            importParametersJson.parameter.Add(new Parameter() { name = "inputFormat", valueString = "application/fhir+ndjson" });
                            importParametersJson.parameter.Add(new Parameter() { name = "mode", valueString = "InitialLoad" });

                            string resourceType = Path.GetFileNameWithoutExtension(blobHierarchyItem.Blob.Name);
                            Parameter p = new() { name = "input", part = new Part[2] };
                            p.part[0] = new Part() { name = "type", valueString = resourceType };
                            p.part[1] = new Part() { name = "url", valueUri = $"{inputUrl}/{inputBlobContainerName}/{blobHierarchyItem.Blob.Name}" };
                            importParametersJson.parameter.Add(p);

                            importParametersJson.parameter.Add(new Parameter() { name = "storageDetail", part = new Part[1] { new Part() { name = "type", valueString = "azure-blob" } } });
                            JsonSerializerOptions options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
                            string importParametersJsonString = JsonSerializer.Serialize(importParametersJson, options);

                            currentResult.importParameters = importParametersJson;

                            // Make server call using import parameter.
                            Console.WriteLine($"  Importing {currentResult.importParameters}");
                            while (await ImportSingle(uri, importParametersJsonString, client, currentResult))
                            {
                                await Task.Delay(500); // Server busy, retry later.
                            }
                        }
                        catch (Exception ex)
                        {
                            currentResult.responseSuccess = false;
                            currentResult.error = ex.Message;
                        }

                        success &= currentResult.responseSuccess;
                    }
                }
            }

            JsonSerializerOptions resultOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true };
            string resultString = JsonSerializer.Serialize(result, resultOptions);
            File.WriteAllText(importResultFileName, resultString);
            if (success)
            {
                Console.WriteLine($"Server import succeeded. Use file '{importResultFileName}' to check if server inserted data into the database.");
            }
            else
            {
                Console.WriteLine($"Server import failed. See file '{importResultFileName}' for more info.");
            }
        }

        private static async Task ImportMultipleBlobsPerServerCall(string serverUrl, string resourceGroupCountStr, string inputUrl, string inputBlobContainerName, string importResultFileName, string inputConnectionString)
        {
            if (inputUrl.EndsWith('/'))
            {
                throw new FHIRDataSynthException($"Input url {inputUrl} must not end with '/'");
            }

            if (serverUrl.EndsWith('/'))
            {
                throw new FHIRDataSynthException($"Server url {serverUrl} must not end with '/'");
            }

            if (!int.TryParse(resourceGroupCountStr, out int resourceGroupCount))
            {
                throw new FHIRDataSynthException($"Resource group count {resourceGroupCount} is not valid int.");
            }

            if (resourceGroupCount < 1)
            {
                throw new FHIRDataSynthException($"Resource group count {resourceGroupCount} is invalid , must be greater than 0.");
            }

            SortedSet<string> dirsAll = await BlobResourceProcessor.GetResourceGroupDirsAsync(inputConnectionString, inputBlobContainerName);
            if (dirsAll.Count < resourceGroupCount)
            {
                throw new FHIRDataSynthException($"Tried to import {resourceGroupCount} resource group(s), but {inputBlobContainerName} contains only {dirsAll.Count}.");
            }

            Uri uri = new Uri(serverUrl + "/$import");
            if (uri.Scheme != "http")
            {
                Console.WriteLine($"WARNING, server may not be able to accept '{uri.Scheme}' requests, try 'http' if failure.");
            }

            SortedSet<string> dirs = new SortedSet<string>();
            foreach (string dir in dirsAll)
            {
                if (resourceGroupCount < 1)
                {
                    break; // TODO: if we add extra loop move this
                }

                dirs.Add(dir);
                resourceGroupCount--;
            }

            JsonSerializerOptions options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

            bool success = true;
            BlobServiceClient blobServiceClient = new BlobServiceClient(inputConnectionString);
            BlobContainerClient blobContainerClient = blobServiceClient.GetBlobContainerClient(inputBlobContainerName);
            ImportResultCollection result = new ImportResultCollection();
            result.importResult = new List<ImportResult>();
            using (HttpClient client = new HttpClient())
            {
                // Potential extra loop to limit number of resource groups in a single call.
                {
                    ImportResult currentResult = new ImportResult();
                    result.importResult.Add(currentResult);
                    currentResult.responseSuccess = true;

                    // Create server call import parameter.
                    ImportParameters importParametersJson = new ImportParameters() { resourceType = "Parameters", parameter = new List<Parameter>() };
                    importParametersJson.parameter.Add(new Parameter() { name = "inputFormat", valueString = "application/fhir+ndjson" });
                    importParametersJson.parameter.Add(new Parameter() { name = "mode", valueString = "InitialLoad" });

                    foreach (string dir in dirs)
                    {
                        await foreach (BlobHierarchyItem blobHierarchyItem in blobContainerClient.GetBlobsByHierarchyAsync(prefix: dir))
                        {
                            string resourceType = Path.GetFileNameWithoutExtension(blobHierarchyItem.Blob.Name);
                            Parameter p = new() { name = "input", part = new Part[2] };
                            p.part[0] = new Part() { name = "type", valueString = resourceType };
                            p.part[1] = new Part() { name = "url", valueUri = $"{inputUrl}/{inputBlobContainerName}/{blobHierarchyItem.Blob.Name}" };
                            importParametersJson.parameter.Add(p);
                        }
                    }

                    importParametersJson.parameter.Add(new Parameter() { name = "storageDetail", part = new Part[1] { new Part() { name = "type", valueString = "azure-blob" } } });
                    string importParametersJsonString = JsonSerializer.Serialize(importParametersJson, options);

                    currentResult.importParameters = importParametersJson;

                    // Make server call using import parameter.
                    Console.WriteLine($"  Server import call...");
                    try // Main reason for this try block is to capture SendAsync exceptions.
                    {
                        while (await ImportSingle(uri, importParametersJsonString, client, currentResult))
                        {
                            await Task.Delay(500); // Server busy, retry later.
                        }
                    }
                    catch (Exception ex)
                    {
                        currentResult.responseSuccess = false;
                        currentResult.error = ex.Message;
                    }

                    success &= currentResult.responseSuccess;
                }
            }

            JsonSerializerOptions resultOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true };
            string resultString = JsonSerializer.Serialize(result, resultOptions);
            File.WriteAllText(importResultFileName, resultString);
            if (success)
            {
                Console.WriteLine($"Server import succeeded. Use file '{importResultFileName}' to check if server inserted data into the database.");
            }
            else
            {
                Console.WriteLine($"Server import failed. See file '{importResultFileName}' for more info.");
            }
        }

        public static async Task Import(string serverUrl, string resourceGroupCountStr, string inputUrl, string inputBlobContainerName, string importResultFileName, string inputConnectionString)
        {
            // await ImportSingleBlobPerServerCall(serverUrl, resourceGroupCountStr, inputUrl, inputBlobContainerName, importResultFileName, inputConnectionString);
            await ImportMultipleBlobsPerServerCall(serverUrl, resourceGroupCountStr, inputUrl, inputBlobContainerName, importResultFileName, inputConnectionString);
        }

        public static async Task<bool> IsImportFinished(string importResultFileName)
        {
            using (HttpClient client = new HttpClient())
            {
                string resultString = File.ReadAllText(importResultFileName);
                ImportResultCollection resultCollection = JsonSerializer.Deserialize<ImportResultCollection>(resultString);
                bool error = false;
                bool resultNotReady = false;
                foreach (ImportResult importResult in resultCollection.importResult)
                {
                    Uri uri = new Uri(importResult.importResultUrl);
                    using (HttpResponseMessage response = await client.GetAsync(uri))
                    {
                        response.EnsureSuccessStatusCode();
                        string responseBody = await response.Content.ReadAsStringAsync();
                        responseBody = responseBody.Trim();
                        if (responseBody == null || responseBody.Length == 0)
                        {
                            resultNotReady = true;
                            continue;
                        }

                        ServerImportResult serverImportResult = JsonSerializer.Deserialize<ServerImportResult>(responseBody);
                        if (serverImportResult.error != null && serverImportResult.error.Length != 0)
                        {
                            error = true;
                            continue;
                        }
                    }
                }

                if (error)
                {
                    throw new FHIRDataSynthException("Import failed, errors detected.");
                }

                if (resultNotReady)
                {
                    Console.WriteLine("Import results not available, try later.");
                    return false;
                }
            }

            Console.WriteLine("Success, import finished.");
            return true;
        }

#pragma warning disable SA1300 // JSON serialization/de-serialization, follow JSON naming convention.
        public sealed class ImportParameters
        {
            public string resourceType { get; set; }

            public List<Parameter> parameter { get; set; }
        }

        public sealed class Parameter
        {
            public string name { get; set; }

            public string valueString { get; set; }

            public Part[] part { get; set; }
        }

        public sealed class Part
        {
            public string name { get; set; }

            public string valueString { get; set; }

            public string valueUri { get; set; }
        }

        public sealed class ImportResult
        {
            public ImportParameters importParameters { get; set; }

            public bool responseSuccess { get; set; }

            public int? responseStatusCode { get; set; }

            public string responseStatusCodeString { get; set; }

            public string responseReasonPhrase { get; set; }

            public string error { get; set; }

            public string importResultUrl { get; set; }
        }

        public sealed class ImportResultCollection
        {
            public List<ImportResult> importResult { get; set; }
        }

#pragma warning disable CA1812 // Code analyzer does not recognize that class is instantiated by JSON de-serializer.
        public sealed class ServerImportResult
#pragma warning restore CA1812
        {
            public DateTime transactionTime { get; set; }

            public string request { get; set; }

            public Output[] output { get; set; }

            public Error[] error { get; set; }
        }

#pragma warning disable CA1812 // Code analyzer does not recognize that class is instantiated by JSON de-serializer.
        public sealed class Output
#pragma warning restore CA1812
        {
            public string type { get; set; }

            public int count { get; set; }

            public string inputUrl { get; set; }
        }

#pragma warning disable CA1812 // Code analyzer does not recognize that class is instantiated by JSON de-serializer.
        public sealed class Error
#pragma warning restore CA1812
        {
            public string type { get; set; }

            public int count { get; set; }

            public string inputUrl { get; set; }

            public string url { get; set; }
        }
#pragma warning restore SA1300
    }
}
