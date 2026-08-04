// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// Declares the set of service types that are owned by the root container and shared by every
    /// tenant container, rather than being constructed once per tenant.
    /// </summary>
    /// <remarks>
    /// Services listed here are forwarded into tenant containers as <em>instances</em>. This is
    /// deliberate: <c>Microsoft.Extensions.DependencyInjection</c> disposes services it creates, so a
    /// factory-based forward would cause the first tenant container disposal to dispose a process-wide
    /// singleton. Instance registrations are never disposed by the child container.
    /// </remarks>
    public sealed class TenantSharedServiceRegistry
    {
        private readonly HashSet<Type> _sharedServiceTypes = new();

        /// <summary>
        /// Gets the service types that are shared with every tenant container.
        /// </summary>
        public IReadOnlyCollection<Type> SharedServiceTypes => _sharedServiceTypes.ToArray();

        /// <summary>
        /// Declares <typeparamref name="TService"/> as shared with every tenant container.
        /// </summary>
        /// <typeparam name="TService">The service type to share.</typeparam>
        /// <returns>This registry, to allow chaining.</returns>
        public TenantSharedServiceRegistry ShareWithTenants<TService>() => ShareWithTenants(typeof(TService));

        /// <summary>
        /// Declares <paramref name="serviceType"/> as shared with every tenant container.
        /// </summary>
        /// <param name="serviceType">The service type to share.</param>
        /// <returns>This registry, to allow chaining.</returns>
        public TenantSharedServiceRegistry ShareWithTenants(Type serviceType)
        {
            EnsureArg.IsNotNull(serviceType, nameof(serviceType));

            _sharedServiceTypes.Add(serviceType);
            return this;
        }

        /// <summary>
        /// Determines whether <paramref name="serviceType"/> is shared with tenant containers.
        /// </summary>
        /// <param name="serviceType">The service type to test.</param>
        /// <returns><c>true</c> if the type is shared; otherwise <c>false</c>.</returns>
        public bool IsShared(Type serviceType)
        {
            EnsureArg.IsNotNull(serviceType, nameof(serviceType));

            return _sharedServiceTypes.Contains(serviceType);
        }
    }
}
