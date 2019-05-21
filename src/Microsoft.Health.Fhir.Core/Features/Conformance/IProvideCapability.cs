// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    /// <summary>
    /// Allows a component to register its FHIR capabilities into a specified ListedCapabilityStatement.
    /// </summary>
    public interface IProvideCapability
    {
        /// <summary>
        /// Allows this component to add capabilities into the specified ListedCapabilityStatement.
        /// </summary>
        /// <param name="statement">The ListedCapabilityStatement to be added to.</param>
        void Build(IListedCapabilityStatement statement);
    }
}
