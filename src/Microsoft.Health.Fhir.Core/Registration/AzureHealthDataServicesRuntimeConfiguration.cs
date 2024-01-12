// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features;

namespace Microsoft.Health.Fhir.Core.Registration
{
    public class AzureHealthDataServicesRuntimeConfiguration : IFhirRuntimeConfiguration
    {
        public string DataStore => KnownDataStores.SqlServer;

        public bool IsSelectiveSearchParameterSupported => true;

        public bool IsExportBackgroundWorkerSupported => false;

        public bool IsCustomerKeyValidationBackgroundWorkerSupported => true;

        public bool IsTransactionSupported => true;
    }
}
