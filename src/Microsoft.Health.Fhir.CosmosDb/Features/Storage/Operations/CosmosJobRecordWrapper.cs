// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.CosmosDb.Features.Storage;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Operations
{
    /// <summary>
    /// A base wrapper for job record wrapper classes that contain metadata specific to CosmosDb.
    /// </summary>
    internal abstract class CosmosJobRecordWrapper : SystemData
    {
        [JsonConstructor]
        protected CosmosJobRecordWrapper()
        {
        }

        [JsonProperty(KnownDocumentProperties.PartitionKey)]
        public abstract string PartitionKey { get; }
    }
}
