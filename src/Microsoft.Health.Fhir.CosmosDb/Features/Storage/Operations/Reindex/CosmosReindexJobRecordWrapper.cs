﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Operations.Reindex
{
    /// <summary>
    /// A wrapper around the <see cref="ReindexJobRecord"/> class that contains metadata specific to CosmosDb.
    /// </summary>
    internal class CosmosReindexJobRecordWrapper : CosmosJobRecordWrapper
    {
        public CosmosReindexJobRecordWrapper(ReindexJobRecord reindexJobRecord)
        {
            EnsureArg.IsNotNull(reindexJobRecord, nameof(reindexJobRecord));

            JobRecord = reindexJobRecord;
            Id = reindexJobRecord.Id;
        }

        [JsonConstructor]
        protected CosmosReindexJobRecordWrapper()
        {
        }

        [JsonProperty(KnownDocumentProperties.PartitionKey)]
        public override string PartitionKey { get; } = CosmosDbReindexConstants.ReindexJobPartitionKey;

        [JsonProperty(JobRecordProperties.JobRecord)]
        public ReindexJobRecord JobRecord { get; private set; }
    }
}
