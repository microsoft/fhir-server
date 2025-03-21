// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Operations.LegacyExport
{
    /// <summary>
    /// A wrapper around the <see cref="ExportJobRecord"/> class that contains metadata specific to CosmosDb.
    /// </summary>
    internal class CosmosLegacyExportJobRecordWrapper : CosmosJobRecordWrapper
    {
        public CosmosLegacyExportJobRecordWrapper(ExportJobRecord exportJobRecord)
        {
            EnsureArg.IsNotNull(exportJobRecord, nameof(exportJobRecord));

            JobRecord = exportJobRecord;
            Id = exportJobRecord.Id;
        }

        [JsonConstructor]
        protected CosmosLegacyExportJobRecordWrapper()
        {
        }

        [JsonProperty(KnownDocumentProperties.PartitionKey)]
        public override string PartitionKey { get; } = CosmosDbLegacyExportConstants.ExportJobPartitionKey;

        [JsonProperty(JobRecordProperties.JobRecord)]
        public ExportJobRecord JobRecord { get; private set; }
    }
}
