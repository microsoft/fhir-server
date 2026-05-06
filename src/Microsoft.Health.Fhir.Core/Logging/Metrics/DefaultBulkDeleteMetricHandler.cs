// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.Metrics;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    public sealed class DefaultBulkDeleteMetricHandler : BaseSuccessRateMetricHandler, IBulkDeleteMetricHandler
    {
        public DefaultBulkDeleteMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory, successMetricName: "BulkDelete.Success", failureMetricName: "BulkDelete.Failure")
        {
        }
    }
}
