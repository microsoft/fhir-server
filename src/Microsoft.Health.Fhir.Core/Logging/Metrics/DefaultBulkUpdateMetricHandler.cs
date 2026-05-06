// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.Metrics;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    public sealed class DefaultBulkUpdateMetricHandler : BaseSuccessRateMetricHandler, IBulkUpdateMetricHandler
    {
        public DefaultBulkUpdateMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory, successMetricName: "BulkUpdate.Success", failureMetricName: "BulkUpdate.Failure")
        {
        }
    }
}
