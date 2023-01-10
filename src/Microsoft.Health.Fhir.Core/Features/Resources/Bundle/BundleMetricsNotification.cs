// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Metrics;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Bundle
{
    public class BundleMetricsNotification : IMetricsNotification
    {
        public BundleMetricsNotification(IDictionary<string, List<BundleSubCallMetricData>> apiCallResults, string bundleType)
        {
            FhirOperation = bundleType;
            ResourceType = KnownResourceTypes.Bundle;
            ApiCallResults = apiCallResults;
        }

        public string FhirOperation { get; }

        public string ResourceType { get; }

        public IDictionary<string, List<BundleSubCallMetricData>> ApiCallResults { get; }
    }
}
