// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Metrics;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate.Messages
{
    public class BulkUpdateMetricsNotification : IMetricsNotification
    {
        public BulkUpdateMetricsNotification(long id, long resourcesUpdated)
        {
            FhirOperation = AuditEventSubType.BulkUpdate;
            ResourceType = null;

            JobId = id;
            ResourcesUpdated = resourcesUpdated;
            Status = OperationStatus.Completed;
        }

        public string FhirOperation { get; }

        public string ResourceType { get; }

        public long ResourcesUpdated { get; }

        public long JobId { get; }

        public OperationStatus Status { get; }
    }
}
