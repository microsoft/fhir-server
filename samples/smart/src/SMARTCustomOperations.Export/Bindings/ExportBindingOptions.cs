// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace SMARTCustomOperations.Export.Bindings
{
    public class ExportBindingOptions
    {
        // Example: https://<workspace>-<fhir>.fhir.azurehealthcareapis.com
        public string? FhirServerEndpoint { get; set; }

        // Example: https://<account>.blob.core.windows.net
        public string? StorageEndpoint { get; set; }

        public string ApiManagementFhirPrefex { get; set; } = "smart";
    }
}
