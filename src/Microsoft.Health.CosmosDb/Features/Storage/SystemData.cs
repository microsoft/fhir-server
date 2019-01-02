// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Newtonsoft.Json;

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    /// <summary>
    /// Defines a base object for serializing System/Meta information to a CosmosDb collection
    /// </summary>
    public abstract class SystemData
    {
        [JsonProperty(KnownDocumentProperties.Id)]
        public string Id { get; set; }

        [JsonProperty(KnownDocumentProperties.ETag)]
        public string ETag { get; set; }

        [JsonProperty(KnownDocumentProperties.Timestamp)]
        public long DocumentTimestamp { get; set; }

        /// <summary>
        /// Gets a value indicating whether this object is system metadata and not user generated data
        /// </summary>
        [JsonProperty(KnownDocumentProperties.IsSystem)]
        public bool IsSystem { get; } = true;

        [JsonProperty(KnownDocumentProperties.SelfLink)]
        public string SelfLink { get; set; }
    }
}
