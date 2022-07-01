// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Metrics;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class ExportTaskMetricsNotification : IMetricsNotification
    {
        public ExportTaskMetricsNotification(ExportJobRecord exportJobRecord)
        {
            FhirOperation = AuditEventSubType.Export;
            ResourceType = null;

            Id = exportJobRecord.Id ?? string.Empty;
            Status = exportJobRecord.Status.ToString();
            QueuedTime = exportJobRecord.QueuedTime;
            EndTime = exportJobRecord.EndTime;
            DataSize = exportJobRecord.Output?.Values.Sum(fileList => fileList.Sum(job => job?.CommittedBytes ?? 0)) ?? 0;
            IsAnonymizedExport = !string.IsNullOrEmpty(exportJobRecord.AnonymizationConfigurationLocation);
            IsAcrMode = !string.IsNullOrEmpty(exportJobRecord.AnonymizationConfigurationCollectionReference);
        }

        public string FhirOperation { get; }

        public string ResourceType { get; }

        public string Id { get; }

        public string Status { get; }

        public DateTimeOffset QueuedTime { get; }

        public DateTimeOffset? EndTime { get; }

        public long DataSize { get; }

        public bool IsAnonymizedExport { get; }

        public bool IsAcrMode { get; }
    }
}
