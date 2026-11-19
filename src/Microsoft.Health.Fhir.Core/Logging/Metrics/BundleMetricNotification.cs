// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    public sealed class BundleMetricNotification : ILatencyMetricNotification
    {
        public BundleMetricNotification(long elapsedMilliseconds, BundleComplexity complexity = BundleComplexity.Low)
        {
            EnsureArg.IsGte(elapsedMilliseconds, 0, nameof(elapsedMilliseconds));

            ElapsedMilliseconds = elapsedMilliseconds;
            Complexity = complexity;
        }

        public long ElapsedMilliseconds { get; set; }

        public BundleComplexity Complexity { get; set; }
    }
}
