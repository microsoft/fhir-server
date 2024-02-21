// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Metrics;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportBundleMetricsNotification : IMetricsNotification
    {
        public ImportBundleMetricsNotification(
            DateTimeOffset startTime,
            DateTimeOffset endTime,
            long succeededCount)
        {
            FhirOperation = AuditEventSubType.ImportBundle;
            ResourceType = null;

            StartTime = startTime;
            EndTime = endTime;
            SucceededCount = succeededCount;
        }

        public string FhirOperation { get; }

        public string ResourceType { get; }

        public DateTimeOffset StartTime { get; }

        public DateTimeOffset EndTime { get; }

        public long SucceededCount { get; }
    }
}
