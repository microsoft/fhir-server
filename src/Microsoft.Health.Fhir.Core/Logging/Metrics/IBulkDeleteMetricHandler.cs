// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    public interface IBulkDeleteMetricHandler : IJobMetric, ISuccessRateMetricHandler
    {
    }
}
