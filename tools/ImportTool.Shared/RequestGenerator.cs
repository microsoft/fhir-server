// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Task = System.Threading.Tasks.Task;

namespace ImportTool
{
    public static class RequestGenerator
    {
        public static async Task<string> GenerateImportRequest(string connectionString, string prefix)
        {
            Parameters parameters = new Parameters();

            // add fixed parts
            parameters.Add("inputFormat", new FhirString("application/fhir+ndjson"));
            parameters.Add("mode", new FhirString("InitialLoad"));

            CloudStorageAccount storageAccount;
            if (CloudStorageAccount.TryParse(connectionString, out storageAccount))
            {
                CloudBlobClient cloudBlobClient = storageAccount.CreateCloudBlobClient();

                await AddInputPartsFromBlobs(cloudBlobClient, prefix, parameters);
            }

            return new FhirJsonSerializer().SerializeToString(parameters);
        }

        private static async Task AddInputPartsFromBlobs(CloudBlobClient cloudBlobClient, string prefix, Parameters parameters)
        {
            BlobContinuationToken continuationToken = null;
            do
            {
                var segments = await cloudBlobClient.ListBlobsSegmentedAsync(
                    prefix: prefix,
                    useFlatBlobListing: true,
                    blobListingDetails: BlobListingDetails.Metadata,
                    maxResults: 500,
                    currentToken: continuationToken,
                    options: null,
                    operationContext: null);

                foreach (var segment in segments.Results.Cast<CloudBlockBlob>())
                {
                    if (Regex.Match(segment.Name, ".+\\.ndjson$", RegexOptions.IgnoreCase).Success)
                    {
                        string resourceType = await GetResourceType(segment);
                        List<Tuple<string, Base>> inputPart = new List<Tuple<string, Base>>();
                        inputPart.Add(Tuple.Create("type", (Base)new FhirString(resourceType)));
                        inputPart.Add(Tuple.Create("url", (Base)new FhirUri(segment.Uri)));
                        string etag = segment.Properties.ETag.Replace("\"", string.Empty, StringComparison.OrdinalIgnoreCase);
                        inputPart.Add(Tuple.Create("etag", (Base)new FhirString(etag)));
                        parameters.Add("input", inputPart);
                    }
                }
            }
            while (continuationToken != null);

            return;
        }

        // Get the first resouce in file to determine type
        private static async Task<string> GetResourceType(CloudBlockBlob cloudBlockBlob)
        {
            using (var stream = await cloudBlockBlob.OpenReadAsync())
            {
                using (StreamReader reader = new StreamReader(stream))
                {
                    string resource = reader.ReadLine();
                    return ExtractResourceType(resource);
                }
            }
        }

        private static string ExtractResourceType(string content)
        {
            var resource = new FhirJsonParser().Parse<Resource>(content);
            return resource.TypeName;
        }
    }
}
