// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// A handle that keeps a tenant container alive for the duration of a unit of work.
    /// </summary>
    /// <remarks>
    /// The owning container cannot complete disposal while any lease remains outstanding.
    /// </remarks>
    public interface ITenantLease : IDisposable
    {
        /// <summary>
        /// Gets the tenant this lease belongs to.
        /// </summary>
        TenantId TenantId { get; }

        /// <summary>
        /// Gets the tenant's service provider.
        /// </summary>
        IServiceProvider Services { get; }
    }
}
