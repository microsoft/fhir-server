// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Metrics;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Messages
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

        /// <summary>
        /// Gets or sets a content associated with this notification. (e.g. A list of deleted search parameter urls.)
        /// TODO: this should probably be Stream but MediatR doesn't look like cloning a notification before it sends to handlers.
        /// </summary>
        public object Content { get; set; }
    }
}
