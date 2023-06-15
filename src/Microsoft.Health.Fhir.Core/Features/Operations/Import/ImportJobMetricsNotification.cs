// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Metrics;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportJobMetricsNotification : IMetricsNotification
    {
        public ImportJobMetricsNotification(
            string id,
            string status,
            DateTimeOffset createTime,
            DateTimeOffset endTime,
            long? dataSize,
            long? succeededCount,
            long? failedCount,
            ImportMode importMode)
        {
            FhirOperation = AuditEventSubType.Import;
            ResourceType = null;

            Id = id;
            Status = status;
            CreateTime = createTime;
            EndTime = endTime;
            DataSize = dataSize;
            SucceededCount = succeededCount;
            FailedCount = failedCount;
            ImportMode = importMode;
        }

        public string FhirOperation { get; }

        public string ResourceType { get; }

        public string Id { get; }

        public string Status { get; }

        public DateTimeOffset CreateTime { get; }

        public DateTimeOffset EndTime { get; }

        public long? DataSize { get; }

        public long? SucceededCount { get; }

        public long? FailedCount { get; }

        public ImportMode ImportMode { get; }
    }
}
