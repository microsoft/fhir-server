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
    internal class CosmosExportJobRecordWrapper : CosmosJobRecordWrapper
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

        [JsonProperty(KnownDocumentProperties.PartitionKey)]
        public override string PartitionKey { get; } = CosmosDbExportConstants.ExportJobPartitionKey;

        [JsonProperty(JobRecordProperties.JobRecord)]
        public ExportJobRecord JobRecord { get; private set; }
    }
}
