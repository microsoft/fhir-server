// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    public class CosmosStorageContext : IStorageContext
    {
        public CosmosStorageContext(string fhirOperation)
        {
            EnsureArg.IsNotNullOrEmpty(fhirOperation, nameof(fhirOperation));

            FhirOperation = fhirOperation;
        }

        public double TotalRequestCharge { get; set; }

        public long? CollectionSizeUsage { get; set; }

        public int ThrottledCount { get; set; }

        public string FhirOperation { get; private set; }
    }
}
