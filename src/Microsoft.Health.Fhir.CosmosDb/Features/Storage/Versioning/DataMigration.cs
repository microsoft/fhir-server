// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning
{
    public class DataMigration : SystemData
    {
        internal const string DataMigrationPartition = "_dataMigrations";

        public DataMigration(string name, string partitionRangeKey)
        {
            Started = Clock.UtcNow;
            Id = $"datamigration_{name}_{partitionRangeKey}";
            Name = name;
            PartitionRangeKey = partitionRangeKey;
        }

        [JsonConstructor]
        protected DataMigration()
        {
        }

        [JsonProperty("started")]
        public DateTimeOffset Started { get; protected set; }

        [JsonProperty("completed")]
        public DateTimeOffset? Completed { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("lastException")]
        public string LastException { get; set; }

        [JsonProperty(KnownResourceWrapperProperties.PartitionKey)]
        public string PartitionKey { get; } = DataMigrationPartition;

        [JsonProperty("partitionRangeKey")]
        public string PartitionRangeKey { get; set; }
    }
}
