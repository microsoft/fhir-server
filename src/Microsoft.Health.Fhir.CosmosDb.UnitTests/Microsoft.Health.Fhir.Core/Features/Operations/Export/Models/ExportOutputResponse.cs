// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.Models
{
    /// <summary>
    /// Class that represents the information regarding each file that is being exported.
    /// This is the output response that we send back to the client once export is complete.
    /// Subset of <see cref="ExportFileInfo"/>.
    /// </summary>
    public class ExportOutputResponse
    {
        public ExportOutputResponse(string type, Uri fileUri, int count)
        {
            EnsureArg.IsNotNullOrWhiteSpace(type, nameof(type));
            EnsureArg.IsNotNull(fileUri, nameof(fileUri));

            Type = type;
            FileUri = fileUri;
            Count = count;
        }

        [JsonConstructor]
        protected ExportOutputResponse()
        {
        }

        [JsonProperty(JobRecordProperties.Type)]
        public string Type { get; private set; }

        [JsonProperty(JobRecordProperties.Url)]
        public Uri FileUri { get; private set; }

        [JsonProperty(JobRecordProperties.Count)]
        public int Count { get; private set; }
    }
}
