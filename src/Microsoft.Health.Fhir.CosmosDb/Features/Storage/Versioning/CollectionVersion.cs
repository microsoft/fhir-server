// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Persistence;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning
{
    public class CollectionVersion : SystemData
    {
        internal const string CollectionVersionPartition = "_collectionVersions";

        public CollectionVersion()
        {
            Id = "collectionversion";
        }

        [JsonProperty("version")]
        public int Version { get; set; }

        [JsonProperty(KnownResourceWrapperProperties.PartitionKey)]
        public string PartitionKey { get; } = CollectionVersionPartition;
    }
}
