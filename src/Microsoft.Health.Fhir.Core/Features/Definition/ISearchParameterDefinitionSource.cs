// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.ElementModel;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    /// <summary>
    /// Supplies the system-defined search parameter resources that every FHIR server starts from.
    /// </summary>
    /// <remarks>
    /// These resources are process-wide constants for a given FHIR version, so implementations are expected to parse
    /// the underlying content once and return the same instances on every call.
    /// </remarks>
    public interface ISearchParameterDefinitionSource
    {
        /// <summary>
        /// Gets the system-defined search parameter resources.
        /// </summary>
        /// <returns>The parsed search parameter resources. The same instance is returned on every call.</returns>
        IReadOnlyList<ITypedElement> GetSystemSearchParameterResources();
    }
}
