// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.Health.Fhir.Core.Features.Security;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class AuthorizationConfiguration
    {
        public string RolesClaim { get; set; } = "roles";

        public bool Enabled { get; set; }

        public IReadOnlyList<Role> Roles { get; internal set; } = ImmutableList<Role>.Empty;

        public IReadOnlyList<string> ScopesClaim { get; set; } = new List<string>() { "scp" };

        public string FhirUserClaim { get; set; } = "fhirUser";

        public string ExtensionFhirUserClaim { get; set; } = "extension_fhirUser";

        // Default is true: a missing or invalid fhirUser claim for patient/user-context SMART scopes
        // results in a 400 Bad Request rather than silently omitting the patient compartment filter.
        // This prevents a token with patient/* scope but no fhirUser from behaving as a broader user/* scope.
        // Set to false only when a deployment explicitly needs the legacy permissive behaviour.
        public bool ErrorOnMissingFhirUserClaim { get; set; } = true;

        public bool EnableSmartWithoutAuth { get; set; } = false;
    }
}
