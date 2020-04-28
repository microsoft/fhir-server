// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;
using EnsureThat;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Registry
{
    internal class SearchParameterStatusWrapper : SystemData
    {
        private Uri _uri;
        public const string SearchParameterStatusPartitionKey = "__searchparameterstatus__";

        [JsonProperty("uri")]
        public Uri Uri
        {
            get => _uri;
            set
            {
                _uri = value;
                Id = value?.ToString().ComputeHash();
            }
        }

        [JsonProperty("status")]
        public SearchParameterStatus Status { get; set; }

        [JsonProperty("isPartiallySupported")]
        public bool? IsPartiallySupported { get; set; }

        [JsonProperty("lastUpdated")]
        public DateTimeOffset LastUpdated { get; set; }

        [JsonProperty(KnownDocumentProperties.PartitionKey)]
        public string PartitionKey { get; } = SearchParameterStatusPartitionKey;
    }
}
