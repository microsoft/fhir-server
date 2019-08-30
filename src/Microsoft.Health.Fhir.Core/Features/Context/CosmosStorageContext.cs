// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    public class CosmosStorageContext : IStorageContext
    {
        public CosmosStorageContext(string fhirOperation, string resourceType)
        {
            EnsureArg.IsNotNullOrEmpty(fhirOperation, nameof(fhirOperation));

            FhirOperation = fhirOperation;
            ResourceType = resourceType;
            RequestCount = 0;
        }

        public double TotalRequestCharge { get; set; }

        public long? CollectionSizeUsage { get; set; }

        public int ThrottledCount { get; set; }

        public int RequestCount { get; set; }

        public string FhirOperation { get; private set; }

        public string ResourceType { get; private set; }
    }
}
