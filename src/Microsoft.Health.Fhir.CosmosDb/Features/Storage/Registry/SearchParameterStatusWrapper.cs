// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

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

        [JsonProperty("sortStatus")]
        [JsonConverter(typeof(StringEnumConverter))]
        public SortParameterStatus? SortStatus { get; set; }

        [JsonProperty(KnownDocumentProperties.PartitionKey)]
        public string PartitionKey { get; } = SearchParameterStatusPartitionKey;
    }
}
