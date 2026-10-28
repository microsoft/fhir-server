// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Describes whether the SMART compartment Device restriction can be enforced for the current
    /// configuration and search parameter status.
    /// </summary>
    public enum SmartCompartmentDeviceRestrictionState
    {
        /// <summary>
        /// The restriction does not apply: it is disabled by configuration, the data store does not support it,
        /// or the active FHIR version does not define a Device patient linkage. Device is treated as a
        /// universally shared resource type, which is the behavior these deployments already have.
        /// </summary>
        NotApplicable,

        /// <summary>
        /// The restriction applies and the search parameter backing it is enabled, so its index is complete
        /// enough to distinguish an unassigned Device from a Device assigned to another compartment.
        /// </summary>
        Enforceable,

        /// <summary>
        /// The restriction applies but the search parameter backing it is unavailable, so the absence of an
        /// index entry no longer proves that a Device is unassigned. Device visibility must fail closed.
        /// </summary>
        Unenforceable,
    }
}
