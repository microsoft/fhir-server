// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Polly;

namespace Microsoft.Health.Internal.FhirLoader
{
    public static class Program
    {
        public static void Main(
            string inputFolder,
            Uri fhirServerUrl,
            Uri authority = null,
            string clientId = null,
            string clientSecret = null,
            string accessToken = null,
            string bufferFileName = "resources.json",
            bool reCreateBufferIfExists = false,
            bool forcePost = false,
            int maxDegreeOfParallelism = 8,
            int refreshInterval = 5)
        {
            var httpClient = new HttpClient();
            var metrics = new MetricsCollector();

            // Create an ndjson file from the FHIR bundles in folder
            if (!new FileInfo(bufferFileName).Exists || reCreateBufferIfExists)
            {
                Console.WriteLine("Creating ndjson buffer file...");
                CreateBufferFile(inputFolder, bufferFileName);
                Console.WriteLine("Buffer created.");
            }

            bool useAuth = authority != null && clientId != null && clientSecret != null && accessToken == null;

            AuthenticationContext authContext =
                useAuth ? new AuthenticationContext(authority.AbsoluteUri, new TokenCache()) : null;
            ClientCredential clientCredential = useAuth ? new ClientCredential(clientId, clientSecret) : null;

            var randomGenerator = new Random();

            var actionBlock = new ActionBlock<string>(
                async resourceString =>
                {
                    var resource = JObject.Parse(resourceString);
                    string resource_type = (string)resource["resourceType"];
                    string id = (string)resource["id"];

                    Thread.Sleep(TimeSpan.FromMilliseconds(randomGenerator.Next(50)));

                    var content = new StringContent(resourceString, Encoding.UTF8, "application/json");
                    TimeSpan[] pollyDelays =
                        new[]
                        {
                            TimeSpan.FromMilliseconds(2000 + randomGenerator.Next(50)),
                            TimeSpan.FromMilliseconds(3000 + randomGenerator.Next(50)),
                            TimeSpan.FromMilliseconds(5000 + randomGenerator.Next(50)),
                            TimeSpan.FromMilliseconds(8000 + randomGenerator.Next(50)),
                            TimeSpan.FromMilliseconds(12000 + randomGenerator.Next(50)),
                            TimeSpan.FromMilliseconds(16000 + randomGenerator.Next(50)),
                        };

                    HttpResponseMessage uploadResult = await Policy
                        .HandleResult<HttpResponseMessage>(response => !response.IsSuccessStatusCode)
                        .WaitAndRetryAsync(pollyDelays, (result, timeSpan, retryCount, context) =>
                        {
                            if (retryCount > 3)
                            {
                                Console.WriteLine(
                                    $"Request failed with {result.Result.StatusCode}. Waiting {timeSpan} before next retry. Retry attempt {retryCount}");
                            }
                        })
                        .ExecuteAsync(() =>
                        {
                            HttpRequestMessage message = forcePost || string.IsNullOrEmpty(id)
                                ? new HttpRequestMessage(HttpMethod.Post, new Uri(fhirServerUrl, $"/{resource_type}"))
                                : new HttpRequestMessage(HttpMethod.Put,  new Uri(fhirServerUrl, $"/{resource_type}/{id}"));

                            message.Content = content;

                            if (useAuth)
                            {
                                AuthenticationResult authResult = authContext
                                    .AcquireTokenAsync(fhirServerUrl.AbsoluteUri.TrimEnd('/'), clientCredential).Result;
                                message.Headers.Authorization =
                                    new AuthenticationHeaderValue("Bearer", authResult.AccessToken);
                            }
                            else if (accessToken != null)
                            {
                                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                            }

                            return httpClient.SendAsync(message);
                        });

                    if (!uploadResult.IsSuccessStatusCode)
                    {
                        string resultContent = await uploadResult.Content.ReadAsStringAsync();
                        Console.WriteLine(resultContent);
                        throw new Exception($"Unable to upload to server. Error code {uploadResult.StatusCode}");
                    }

                    metrics.Collect(DateTime.Now);
                },
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = maxDegreeOfParallelism,
                });

            // Start output on timer
            var t = new Task(() =>
            {
                while (true)
                {
                    Thread.Sleep(1000 * refreshInterval);
                    Console.WriteLine($"Resources per second: {metrics.EventsPerSecond}");
                }
            });
            t.Start();

            // Read the ndjson file and feed it to the threads
            var buffer = new StreamReader(bufferFileName);
            string line;
            while ((line = buffer.ReadLine()) != null)
            {
                actionBlock.Post(line);
            }

            actionBlock.Complete();
            actionBlock.Completion.Wait();
        }

        private static void CreateBufferFile(string inputFolder, string bufferFileName)
        {
            using (var outFile = new StreamWriter(bufferFileName))
            {
                string[] files = Directory.GetFiles(inputFolder, "*.json", SearchOption.TopDirectoryOnly);

                foreach (string file in files)
                {
                    string bundleText = File.ReadAllText(file);

                    JObject bundle;
                    try
                    {
                        bundle = JObject.Parse(bundleText);
                    }
                    catch (JsonReaderException)
                    {
                        Console.WriteLine("Input file is not a valid JSON document");
                        throw;
                    }

                    try
                    {
                        SyntheaReferenceResolver.GivenConvertUuiDs(bundle);
                    }
                    catch
                    {
                        Console.WriteLine("Failed to resolve references in doc");
                        throw;
                    }

                    foreach (JToken r in bundle.SelectTokens("$.entry[*].resource"))
                    {
                        outFile.WriteLine(r.ToString(Formatting.None));
                    }
                }
            }
        }
    }
}
