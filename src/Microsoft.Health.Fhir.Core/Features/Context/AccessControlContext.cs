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
        /// <summary>
        /// Value indicated whether or not fine grained access control policies should be applied
        /// </summary>
        public bool ApplyFineGrainedAccessControl { get; set; } = false;

        /// <summary>
        /// the string values that were passed in as scopes
        /// </summary>
        public ICollection<string> ClinicalScopes { get; } = new List<string>();

        /// <summary>
        /// A collection of the allowed resource types and which action is allowed on that type
        /// </summary>
        public ICollection<ScopeRestriction> AllowedResourceActions { get; } = new List<ScopeRestriction>();

        /// <summary>
        /// A uri which points to a specific fhir resource in this Fhir server, and is associated with the current user
        /// </summary>
        public Uri FhirUserClaim { get; set; } = null;

        /// <summary>
        /// For a SMART on FHIR request, this is the resource type of the user's compartment.  Searches will be restricted to this compartment.
        /// </summary>
        public string CompartmentResourceType { get; set; } = null;

        /// <summary>
        /// For a SMART on FHIR request, this is the id of the resource which is associated with the user, and
        /// identifies the user's compartment.  Searches will be restricted to this compartment.
        /// </summary>
        public string CompartmentId { get; set; } = null;
    }
}
