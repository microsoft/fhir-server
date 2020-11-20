// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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

            ExportJobRecord = exportJobRecord;
        }

        public string FhirOperation { get; }

        public string ResourceType { get; }

        public ExportJobRecord ExportJobRecord { get; }
    }
}
