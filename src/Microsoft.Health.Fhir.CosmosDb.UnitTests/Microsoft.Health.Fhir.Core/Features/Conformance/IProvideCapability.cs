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
        /// Allows this component to configure the server's CapabilityStatement.
        /// </summary>
        /// <param name="builder">Instance of the CapabilityStatementBuilder.</param>
        void Build(ICapabilityStatementBuilder builder);
    }
}
