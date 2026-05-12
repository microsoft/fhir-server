// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.Metrics;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics.Handlers
{
    public sealed class DefaultExportMetricHandler : BaseSuccessRateMetricHandler, IExportMetricHandler
    {
        public DefaultExportMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory, successMetricName: "Export.Success", failureMetricName: "Export.Failure")
        {
        }
    }
}
