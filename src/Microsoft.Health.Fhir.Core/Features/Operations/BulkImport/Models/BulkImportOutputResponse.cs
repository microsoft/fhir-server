// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkImport.Models
{
    /// <summary>
    /// This is the output response that we send back to the client once bulk import is complete.
    /// Subset of <see cref="BulkImportFileInfo"/>.
    /// </summary>
    public class BulkImportOutputResponse
    {
        public BulkImportOutputResponse(Uri inputUrl, int count, Uri url, string type)
        {
            EnsureArg.IsNotNull(inputUrl, nameof(inputUrl));
            if (url != null)
            {
                EnsureArg.IsNotNullOrWhiteSpace(Type, nameof(Type));
            }

            InputUrl = inputUrl;
            Count = count;
            Url = url;
            Type = type;
        }

        [JsonConstructor]
        protected BulkImportOutputResponse()
        {
        }

        [JsonProperty(JobRecordProperties.InputUrl)]
        public Uri InputUrl { get; private set; }

        [JsonProperty(JobRecordProperties.Count)]
        public int Count { get; private set; }

        [JsonProperty(JobRecordProperties.Url)]
        public Uri Url { get; private set; }

        [JsonProperty(JobRecordProperties.Type)]
        public string Type { get; private set; }
    }
}
