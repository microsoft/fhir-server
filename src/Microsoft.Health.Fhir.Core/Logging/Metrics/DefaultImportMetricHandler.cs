// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.Metrics;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    public sealed class DefaultImportMetricHandler : BaseSuccessRateMetricHandler, IImportMetricHandler
    {
        public DefaultImportMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory, successMetricName: "Import.Success", failureMetricName: "Import.Failure")
        {
        }
    }
}
