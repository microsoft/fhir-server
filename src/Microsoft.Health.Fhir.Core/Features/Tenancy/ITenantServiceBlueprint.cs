// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// Provides access to the service registrations that make up the root composition root so tenant
    /// containers can be constructed from the same registrations later.
    /// </summary>
    public interface ITenantServiceBlueprint
    {
        /// <summary>
        /// Creates a point-in-time copy of the root service registrations.
        /// </summary>
        /// <returns>A new list containing the root <see cref="ServiceDescriptor"/> instances.</returns>
        IReadOnlyList<ServiceDescriptor> CreateSnapshot();
    }
}
