// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    /// <summary>
    /// Represents an extension of the SearchParameter that displays the current state of enabling for a given search parameter in the CapabilityStatement that is generated when the Metadata endpoint is called.
    ///
    /// The extension has the following constraints:
    /// - URL for the extension: http://hl7.org/fhir/StructureDefinition/SearchParameter-Status
    /// - Status value must be a string
    /// - Status value must be one of the following options: "enabled", "disabled", "deleted", "pendingDisable", "pendingDelete", "supported", "unsupported"
    /// </summary>
    public class SearchParamStatusExtensionComponent : Extension
    {
        public SearchParamStatusExtensionComponent(string status)
        {
            EnsureArg.IsNotNull(status, nameof(status));

            Url = "http://hl7.org/fhir/StructureDefinition/SearchParameter-Status";
            Value = new FhirString(status);
        }
    }
}
