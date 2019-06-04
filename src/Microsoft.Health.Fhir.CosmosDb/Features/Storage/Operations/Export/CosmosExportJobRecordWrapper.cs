// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Operations.Export
{
    /// <summary>
    /// A wrapper around the <see cref="ExportJobRecord"/> class that contains metadata specific to CosmosDb.
    /// </summary>
    internal class CosmosExportJobRecordWrapper
    {
        public CosmosExportJobRecordWrapper(ExportJobRecord exportJobRecord)
        {
            EnsureArg.IsNotNull(exportJobRecord, nameof(exportJobRecord));

            JobRecord = exportJobRecord;
            Id = exportJobRecord.Id;
        }

        [JsonConstructor]
        protected CosmosExportJobRecordWrapper()
        {
        }

        [JsonProperty(JobRecordProperties.JobRecord)]
        public ExportJobRecord JobRecord { get; private set; }

        [JsonProperty(KnownDocumentProperties.PartitionKey)]
        public string PartitionKey { get; } = CosmosDbExportConstants.ExportJobPartitionKey;

        [JsonProperty(KnownDocumentProperties.Id)]
        public string Id { get; set; }

        [JsonProperty(KnownDocumentProperties.ETag)]
        public string ETag { get; protected set; }

        [JsonProperty(KnownDocumentProperties.IsSystem)]
        public bool IsSystem { get; } = true;
    }
}
