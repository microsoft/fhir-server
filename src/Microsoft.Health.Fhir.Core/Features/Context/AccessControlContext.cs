// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    public class AccessControlContext
    {
        // Value indicated whether or not fine grained access control policies should be applied
        public bool ApplyFineGrainedAccessControl { get; set; }

        // the string values that were passed in as scopes
        public ICollection<string> ClinicalScopes { get; } = new List<string>();

        // A collection of the allowed resource types and which action is allowed on that type
        public ICollection<ScopeRestriction> AllowedResourceActions { get; } = new List<ScopeRestriction>();

        /// <summary>
        /// A uri which points to a specific fhir resource in this Fhir server, and is associated with the current user
        /// </summary>
        public Uri FhirUserClaim { get; set; }
    }
}
