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
            DateTimeOffset createdTime,
            DateTimeOffset endTime,
            long? dataSize,
            long? succeedCount,
            long? failedCount)
        {
            FhirOperation = AuditEventSubType.Import;
            ResourceType = null;

            Id = id;
            Status = status;
            CreatedTime = createdTime;
            EndTime = endTime;
            DataSize = dataSize;
            SucceedCount = succeedCount;
            FailedCount = failedCount;
        }

        public string FhirOperation { get; }

        public string ResourceType { get; }

        public string Id { get; }

        public string Status { get; }

        public DateTimeOffset CreatedTime { get; }

        public DateTimeOffset EndTime { get; }

        public long? DataSize { get; }

        public long? SucceedCount { get; }

        public long? FailedCount { get; }
    }
}
