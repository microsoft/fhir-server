// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// Default <see cref="ITenantServiceBlueprint"/> implementation that holds a live reference to the
    /// root <see cref="IServiceCollection"/> and copies it on demand.
    /// </summary>
    /// <remarks>
    /// Holding a live reference is intentional. The blueprint is captured while the composition root is
    /// still being assembled, so registrations added after capture are still reflected in tenant
    /// containers.
    /// </remarks>
    public sealed class TenantServiceBlueprint : ITenantServiceBlueprint
    {
        private readonly IServiceCollection _rootServices;

        /// <summary>
        /// Initializes a new instance of the <see cref="TenantServiceBlueprint"/> class.
        /// </summary>
        /// <param name="rootServices">The root service collection.</param>
        public TenantServiceBlueprint(IServiceCollection rootServices)
        {
            EnsureArg.IsNotNull(rootServices, nameof(rootServices));
            _rootServices = rootServices;
        }

        /// <inheritdoc />
        public IReadOnlyList<ServiceDescriptor> CreateSnapshot() => new List<ServiceDescriptor>(_rootServices);
    }
}
