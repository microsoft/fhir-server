// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;

namespace ImportTool
{
    public static class RequestGenerator
    {
        public static async Task<string> GenerateImportRequest(string connectionString, string prefix, int maxFileNumber)
        {
            Parameters parameters = new Parameters();

            // add fixed parts
            parameters.Add("inputFormat", new FhirString("application/fhir+ndjson"));
            parameters.Add("mode", new FhirString("InitialLoad"));

            CloudStorageAccount storageAccount;
            if (CloudStorageAccount.TryParse(connectionString, out storageAccount))
            {
                CloudBlobClient cloudBlobClient = storageAccount.CreateCloudBlobClient();

                (int count, int succeedCount) = await AddInputPartsFromBlobs(cloudBlobClient, prefix, maxFileNumber, parameters);

                Console.WriteLine($"Total files: {count} traversed.");
                Console.WriteLine($"Succeed files: {succeedCount} put as input.");
            }

            return new FhirJsonSerializer().SerializeToString(parameters);
        }

        private static async Task<Tuple<int, int>> AddInputPartsFromBlobs(CloudBlobClient cloudBlobClient, string prefix, int maxFileNumber, Parameters parameters)
        {
            int count = 0;
            int succeed_count = 0;

            BlobContinuationToken continuationToken = null;
            do
            {
                var segments = await cloudBlobClient.ListBlobsSegmentedAsync(
                    prefix: prefix,
                    useFlatBlobListing: true,
                    blobListingDetails: BlobListingDetails.Metadata,
                    maxResults: Math.Min(maxFileNumber, 5000),
                    currentToken: continuationToken,
                    options: null,
                    operationContext: null);

                foreach (var segment in segments.Results.Cast<CloudBlockBlob>())
                {
                    try
                    {
                        string resourceType = await GetResourceType(segment);
                        List<Tuple<string, Base>> inputPart = new List<Tuple<string, Base>>();
                        inputPart.Add(Tuple.Create("type", (Base)new FhirString(resourceType)));
                        inputPart.Add(Tuple.Create("url", (Base)new FhirUri(segment.Uri)));
                        string etag = segment.Properties.ETag.Replace("\"", string.Empty, StringComparison.OrdinalIgnoreCase);
                        inputPart.Add(Tuple.Create("etag", (Base)new FhirString(etag)));
                        parameters.Add("input", inputPart);
                        succeed_count++;
                    }
                    catch (Exception oex)
                    {
                        Console.WriteLine($"Error reslove file {segment.Uri} due to exception {oex.Message}");
                    }

                    count++;
                }
            }
            while (continuationToken != null && count < maxFileNumber);

            return new Tuple<int, int>(count, succeed_count);
        }

        // Get the first resouce in file to determine type
        private static async Task<string> GetResourceType(CloudBlockBlob cloudBlockBlob)
        {
            using (var stream = await cloudBlockBlob.OpenReadAsync())
            {
                char[] buffer = ArrayPool<char>.Shared.Rent(128);
                try
                {
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        reader.Read(buffer, 0, 128);
                        return ExtractResourceType(new string(buffer));
                    }
                }
                finally
                {
                    ArrayPool<char>.Shared.Return(buffer);
                }
            }
        }

        private static string ExtractResourceType(string content)
        {
            Regex regex = new Regex("{\"resourceType\":\"([a-zA-Z]+)\",");
            Match match = regex.Match(content);
            if (!match.Success)
            {
                throw new FormatException();
            }

            return match.Groups[1].Value;
        }
    }
}
