// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.CosmosDb.Features.Storage;
using Newtonsoft.Json;

namespace Microsoft.Health.ControlPlane.CosmosDb.Features.Storage.Rbac
{
    public class CosmosBootstrap : SystemData
    {
        public const string BootstrapPartition = "_bootstrap";

        public CosmosBootstrap()
        {
            Id = "bootstrap";
        }

        [JsonProperty("hash")]
        public string Hash { get; set; }

        [JsonProperty(KnownDocumentProperties.PartitionKey)]
        public string PartitionKey { get; } = BootstrapPartition;
    }
}
