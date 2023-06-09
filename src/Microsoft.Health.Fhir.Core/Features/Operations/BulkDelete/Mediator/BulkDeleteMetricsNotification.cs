// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Metrics;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Mediator
{
    public class BulkDeleteMetricsNotification : IMetricsNotification
    {
        public BulkDeleteMetricsNotification(long id, long resourcesDeleted)
        {
            FhirOperation = AuditEventSubType.BulkDelete;
            ResourceType = null;

            JobId = id;
            ResourcesDeleted = resourcesDeleted;
            Status = OperationStatus.Completed;
        }

        public string FhirOperation { get; }

        public string ResourceType { get; }

        public long ResourcesDeleted { get; }

        public long JobId { get; }

        public OperationStatus Status { get; }
    }
}
