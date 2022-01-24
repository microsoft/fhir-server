// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Metrics;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;
using static Hl7.Fhir.Model.Bundle;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    public class BundleMetricsNotification : IMetricsNotification
    {
        public BundleMetricsNotification(IDictionary<string, int> apiCallResults, BundleType bundleType)
        {
            FhirOperation = bundleType == BundleType.Batch ? AuditEventSubType.Batch : AuditEventSubType.Transaction;
            ResourceType = KnownResourceTypes.Bundle;
            ApiCallResults = apiCallResults;
        }

        public string FhirOperation { get; }

        public string ResourceType { get; }

        public IDictionary<string, int> ApiCallResults { get; }
    }
}
