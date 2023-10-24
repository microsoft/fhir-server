// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features;

namespace Microsoft.Health.Fhir.Core.Registration
{
    public class AzureApiForFhirRuntimeConfiguration : IFhirRuntimeConfiguration
    {
        public string DataStore => KnownDataStores.CosmosDb;

        public bool IsSelectiveSearchParameterSupported => false;

        public bool IsExportBackgroundWorkedSupported => true;

        public bool IsCustomerKeyValidationBackgroudWorkerSupported => false;

        public bool IsTransactionSupported => false;
    }
}
