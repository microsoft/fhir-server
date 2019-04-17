// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Export;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Export
{
    internal class CosmosExportJobRecordWrapper
    {
        public CosmosExportJobRecordWrapper(ExportJobRecord exportJobRecord)
        {
            EnsureArg.IsNotNull(exportJobRecord, nameof(exportJobRecord));

            JobRecord = exportJobRecord;
            Id = exportJobRecord.Id;
        }

        [JsonConstructor]
        private CosmosExportJobRecordWrapper()
        {
        }

        [JsonProperty(JobRecordProperties.JobRecord)]
        public ExportJobRecord JobRecord { get; private set; }

        [JsonProperty(JobRecordProperties.PartitonKey)]
        public string PartitionKey { get; } = OperationsConstants.ExportJobPartitionKey;

        [JsonProperty(JobRecordProperties.Id)]
        public string Id { get; private set; }
    }
}
