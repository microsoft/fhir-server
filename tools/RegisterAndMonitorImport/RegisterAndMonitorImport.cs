// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Internal.Fhir.RegisterAndMonitorImport
{
    internal sealed class RegisterAndMonitorImport
    {
        private readonly RegisterAndMonitorConfiguration monitorConfiguration;
        private static readonly HttpClient HttpClient = new();
        private static BlobContainerClient s_blobContainerClientSource;
        private static List<BlobItem> s_blobItems;
        private static HashSet<string> s_importedBlobNames = new();
        private static readonly string OutputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "importResults");

        // this is a list of all the ndjson file names that have been imported from your blob container
        // thereby helping eliminate duplicate imports of the same blob
        private static readonly string OutputFileName = Path.Combine(OutputDirectory, "importer.txt");

        // this is a list of all urls that were used to get the import status
        private static readonly string LocationUrlFileName = Path.Combine(OutputDirectory, "locationUrls.txt");

        public RegisterAndMonitorImport(RegisterAndMonitorConfiguration configuration)
        {
            monitorConfiguration = configuration;
        }

        internal async Task Run()
        {
            try
            {
                if (monitorConfiguration.IsMonitorImportStatusEndpoint)
                {
                    Console.WriteLine($"Getting the import status for {monitorConfiguration.MonitorImportStatusEndpoint}{Environment.NewLine}");

                    // all attempted Urls for GetImportStatus are appended to the LocationUrlFileName file
                    await GetImportStatus(monitorConfiguration.MonitorImportStatusEndpoint);
                }
                else
                {
                    await Init();

                    // this may take too long and timeout so it may be worth commenting out if you have too many resources
                    // int currentResourceCount = await GetCurrentResourceCount();
                    // Console.WriteLine($"{currentResourceCount:N0} {ResourceType} found");

                    await RunImport();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                await WriteImportedBlobNames();
            }
        }

        private async Task Init()
        {
            if (!Directory.Exists(OutputDirectory))
            {
                Directory.CreateDirectory(OutputDirectory);
            }

            s_blobContainerClientSource = GetContainerClient(monitorConfiguration.ContainerName);
            await LoadImportedBlobItems();
            GetBlobItems();
        }

        private static async Task WriteLocationUrls(string url)
        {
            var names = new HashSet<string>();
            if (File.Exists(LocationUrlFileName))
            {
                var content = await File.ReadAllLinesAsync(LocationUrlFileName);
                names = content.ToHashSet();
            }

            names.Add(url);

            await File.WriteAllTextAsync(LocationUrlFileName, string.Join(Environment.NewLine, names));
        }

        private static async Task LoadImportedBlobItems()
        {
            if (File.Exists(OutputFileName))
            {
                var content = await File.ReadAllLinesAsync(OutputFileName);
                s_importedBlobNames = content.ToHashSet();
            }

            Console.WriteLine($"Found {s_importedBlobNames.Count} blobs already processed.");
        }

        private async Task WriteImportedBlobNames()
        {
            if (File.Exists(OutputFileName))
            {
                File.Delete(OutputFileName);
            }

            if (s_importedBlobNames != null && !monitorConfiguration.IsMonitorImportStatusEndpoint)
            {
                await File.WriteAllTextAsync(OutputFileName, string.Join(Environment.NewLine, s_importedBlobNames));
                Console.WriteLine($"Saved file: {OutputFileName}");
            }
        }

        private void GetBlobItems()
        {
            if (s_blobContainerClientSource != null)
            {
                try
                {
                    s_blobItems = s_blobContainerClientSource.GetBlobs().Where(_ => _.Name.EndsWith($"{monitorConfiguration.ResourceType}.ndjson", true, CultureInfo.CurrentCulture)).ToList();
                    Console.WriteLine($"Total container BlobItems count = {s_blobItems.Count}");

                    foreach (string item in s_importedBlobNames)
                    {
                        BlobItem blobItem = s_blobItems.Where(e => e.Name.Contains(item, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                        if (blobItem != null)
                        {
                            s_blobItems.Remove(blobItem);
                        }
                    }

                    s_blobItems = s_blobItems.Take(monitorConfiguration.NumberOfBlobsForImport).ToList();

                    Console.WriteLine($"Working set of BlobItems count = {s_blobItems.Count}");
                }
                catch (RequestFailedException e)
                {
                    if (e.Status == (int)HttpStatusCode.Forbidden)
                    {
                        Console.WriteLine($"Your connection string {monitorConfiguration.ConnectionString} is invalid. Please verify and try again.{Environment.NewLine}");
                    }

                    throw;
                }
            }
        }

        private async Task RunImport()
        {
            if (s_blobContainerClientSource == null || s_blobItems == null || s_blobItems.Count == 0)
            {
#pragma warning disable CA1303 // Do not pass literals as localized parameters
                Console.WriteLine("No items to import.");
#pragma warning restore CA1303 // Do not pass literals as localized parameters
                return;
            }

            var sb = new StringBuilder();
            sb.Append("{\"resourceType\": \"Parameters\", \"parameter\": [{\"name\": \"inputFormat\",\"valueString\": \"application/fhir+ndjson\"},{\"name\": \"mode\",\"valueString\": \"IncrementalLoad\"},");

            var size = 0L;

            foreach (BlobItem blob in s_blobItems)
            {
                if (blob.Properties.ContentLength != null)
                {
                    size += blob.Properties.ContentLength.Value;
                }

                var type = blob.Name.Split('/')[1].Split('.')[0];
                sb.AppendLine("{\"name\": \"input\",\"part\": [{\"name\": \"type\",\"valueString\": ");
                sb.Append('"');
                sb.Append(type);
                sb.AppendLine("\"");
                sb.AppendLine(" },{\"name\": \"url\",\"valueUri\": ");
                sb.Append('"');
                sb.Append(CultureInfo.CurrentCulture, $"{s_blobContainerClientSource.Uri}/{blob.Name}");
                sb.AppendLine("\"");
                sb.AppendLine("}]},");
            }

            sb.Append("{\"name\": \"storageDetail\",\"part\": [{\"name\": \"type\",\"valueString\": \"azure-blob\"}]}]}");

            Console.WriteLine($"TotalSize for blobs = {size:N0}");

            var json = sb.ToString();
            var data = new StringContent(json, Encoding.UTF8, "application/fhir+json");
            var url = $"{monitorConfiguration.FhirEndpoint}/$import";
            using var requestMessage = new HttpRequestMessage()
            {
                RequestUri = new Uri(url),
                Method = HttpMethod.Post,
                Content = data,
            };

            requestMessage.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/fhir+json"));
            requestMessage.Headers.Add("Prefer", "respond-async");
            HttpResponseMessage response = await GetHttpResponseMessageAsync(requestMessage);

            Console.WriteLine($"response.IsSuccessStatusCode = {response.IsSuccessStatusCode}");
            string content = await response.Content.ReadAsStringAsync();
            Console.WriteLine(content);

            if (response.Content.Headers.Contains("Content-Location"))
            {
                IEnumerable<string> location = response.Content.Headers.GetValues("Content-Location");
                await GetImportStatus(location.First());
            }
        }

        private async Task<HttpResponseMessage> GetHttpResponseMessageAsync(HttpRequestMessage request)
        {
            if (monitorConfiguration.UseBearerToken)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetToken());
            }

            return await HttpClient.SendAsync(request, CancellationToken.None).ConfigureAwait(false);
        }

        private async Task GetImportStatus(string url)
        {
            await WriteLocationUrls(url);

            var swTotalTime = new Stopwatch();
            var swSingleTime = new Stopwatch();
            swTotalTime.Start();
            swSingleTime.Start();

            while (true)
            {
                using var requestMessage = new HttpRequestMessage()
                {
                    RequestUri = new Uri(url),
                    Method = HttpMethod.Get,
                };

                HttpResponseMessage response = await GetHttpResponseMessageAsync(requestMessage);
                string content = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"{Environment.NewLine}GET {requestMessage.RequestUri}");
                Console.WriteLine($"StatusCode = {response.StatusCode}");

                ImportResponse importJson = TryParseJson(content);
                PrintImportResponse(importJson);
                bool addedUrl = SaveImportedUrl(importJson);

                Console.WriteLine($"{(addedUrl ? "Completed" : "Processing")} file elapsed time: {swSingleTime.Elapsed.Duration()}");
                if (addedUrl)
                {
                    swSingleTime.Restart();
                }

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    swTotalTime.Stop();
                    Console.WriteLine($"Completed: Total running time: {swTotalTime.Elapsed.Duration()}");
                    break;
                }
                else if (response.StatusCode == HttpStatusCode.Accepted)
                {
                    Console.WriteLine($"Total running time: {swTotalTime.Elapsed.Duration()} - awaiting {monitorConfiguration.ImportStatusDelay} before retry");
                    await Task.Delay(monitorConfiguration.ImportStatusDelay);
                    continue;
                }
                else
                {
                    Console.WriteLine($"Failed to get expected status for import: {url}");
                    break;
                }
            }

            swSingleTime.Stop();
            swTotalTime.Stop();
        }

        private static bool SaveImportedUrl(ImportResponse response)
        {
            bool addedUrl = false;
            if (response != null && s_blobContainerClientSource != null)
            {
                // in case there are only error conditions and no success in the output
                foreach (ImportResponse.Json r in response.Error)
                {
                    if (s_importedBlobNames.Add(r.InputUrl.Replace(s_blobContainerClientSource.Uri.ToString() + "/", string.Empty, StringComparison.OrdinalIgnoreCase)))
                    {
                        addedUrl = true;
                    }
                }

                foreach (ImportResponse.Json r in response.Output)
                {
                    if (s_importedBlobNames.Add(r.InputUrl.Replace(s_blobContainerClientSource.Uri.ToString() + "/", string.Empty, StringComparison.OrdinalIgnoreCase)))
                    {
                        addedUrl = true;
                    }
                }
            }

            return addedUrl;
        }

        private static void PrintImportResponse(ImportResponse response)
        {
            if (response != null)
            {
                if (response.Output.Count == 0 && response.Error.Count == 0)
                {
#pragma warning disable CA1303 // Do not pass literals as localized parameters
                    Console.WriteLine("No import results to report on");
#pragma warning restore CA1303 // Do not pass literals as localized parameters
                    return;
                }

                PrintResponse(response.Output);
                if (response.Error.Count > 0)
                {
                    Console.BackgroundColor = ConsoleColor.Black;
                    Console.ForegroundColor = ConsoleColor.Red;
                    PrintResponse(response.Error);
                    Console.ResetColor();
                }
            }
        }

        private static void PrintResponse(List<ImportResponse.Json> response)
        {
            foreach (ImportResponse.Json item in response)
            {
                Console.WriteLine($"{string.Format(CultureInfo.CurrentCulture, "{0, 3}", response.IndexOf(item) + 1)} {string.Format(CultureInfo.CurrentCulture, "{0, 10}", item.Count.ToString("N0", CultureInfo.CurrentCulture))}   {item.InputUrl}");
            }
        }

        private static ImportResponse TryParseJson(string value)
        {
            ImportResponse parsedJson = default;

            if (string.IsNullOrWhiteSpace(value))
            {
                return parsedJson;
            }
            else
            {
                value = value.Trim();
                if (value.StartsWith("{", StringComparison.Ordinal) && value.EndsWith("}", StringComparison.Ordinal))
                {
                    try
                    {
                        parsedJson = JsonConvert.DeserializeObject<ImportResponse>(value);
                    }
                    catch (JsonReaderException)
                    {
                    }
                }
            }

            return parsedJson;
        }

        private async Task<int> GetCurrentResourceCount()
        {
            int total = 0;
            try
            {
                HttpClient.Timeout = TimeSpan.FromMinutes(5);
                using var requestMessage = new HttpRequestMessage(HttpMethod.Get, $"{monitorConfiguration.FhirEndpoint}/{monitorConfiguration.ResourceType}?_summary=count");
                using HttpResponseMessage response = await GetHttpResponseMessageAsync(requestMessage);

                string content = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(content))
                {
                    var json = JObject.Parse(content);
                    _ = int.TryParse((string)json["total"], out total);
                }

                if (!response.IsSuccessStatusCode)
                {
#pragma warning disable CA1303 // Do not pass literals as localized parameters
                    Console.WriteLine($"Failed to get success from {nameof(GetCurrentResourceCount)}");
#pragma warning restore CA1303 // Do not pass literals as localized parameters
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            return total;
        }

        private async Task<string> GetToken()
        {
            string accessToken = string.Empty;
            var parameters = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("grant_type", monitorConfiguration.TokenGrantType),
                new KeyValuePair<string, string>("resource", monitorConfiguration.FhirEndpoint),
                new KeyValuePair<string, string>("client_id", monitorConfiguration.TokenClientId),
                new KeyValuePair<string, string>("client_secret", monitorConfiguration.TokenClientSecret),
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, monitorConfiguration.FhirEndpoint)
            {
                Content = new FormUrlEncodedContent(parameters),
            };

            using HttpResponseMessage accessTokenResponse = await HttpClient.SendAsync(request);
            if (!accessTokenResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to get token. Status code: {accessTokenResponse.StatusCode}.");
            }
            else
            {
                string content = await accessTokenResponse.Content.ReadAsStringAsync();
                var json = JObject.Parse(content);
                accessToken = (string)json["access_token"] ?? string.Empty;
            }

            return accessToken;
        }

        private BlobContainerClient GetContainerClient(string containerName)
        {
            try
            {
                return new BlobServiceClient(monitorConfiguration.ConnectionString).GetBlobContainerClient(containerName);
            }
            catch
            {
                Console.WriteLine($"Unable to parse storage reference or connect to storage account {containerName}.");
                throw;
            }
        }
    }
}
