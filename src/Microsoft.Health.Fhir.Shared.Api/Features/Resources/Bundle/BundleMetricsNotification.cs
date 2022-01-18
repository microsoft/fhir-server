// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Metrics;
using Microsoft.Health.Fhir.ValueSets;
using static Hl7.Fhir.Model.Bundle;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    public class BundleMetricsNotification : IMetricsNotification
    {
        public BundleMetricsNotification(IDictionary<string, int> apiCallResults, BundleType bundleType)
        {
            FhirOperation = bundleType == BundleType.Batch ? AuditEventSubType.Batch : AuditEventSubType.Transaction;
            ResourceType = null;
            ApiCallResults = apiCallResults;

            SuccessfulApiCalls = 0;
            ApiCalls = 0;
            foreach (string key in apiCallResults.Keys)
            {
                ApiCalls += apiCallResults[key];
                if (key.StartsWith("2", StringComparison.OrdinalIgnoreCase))
                {
                    SuccessfulApiCalls += apiCallResults[key];
                }
            }
        }

        public string FhirOperation { get; }

        public string ResourceType { get; }

        public int SuccessfulApiCalls { get; }

        public int ApiCalls { get; }

        public IDictionary<string, int> ApiCallResults { get; }

    }
}
