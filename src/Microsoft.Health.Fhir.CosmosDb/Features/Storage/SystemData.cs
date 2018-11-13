// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Persistence;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>
    /// Defines a base object for serializing System/Meta information to a CosmosDb collection
    /// </summary>
    public abstract class SystemData
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("_etag")]
        public string ETag { get; set; }

        [JsonProperty("_ts")]
        public long DocumentTimestamp { get; set; }

        /// <summary>
        /// Gets a value indicating whether this object is system metadata and not user generated data
        /// </summary>
        [JsonProperty(KnownResourceWrapperProperties.IsSystem)]
        public bool IsSystem { get; } = true;

        [JsonProperty("_selfLink")]
        public string SelfLink { get; set; }
    }
}
