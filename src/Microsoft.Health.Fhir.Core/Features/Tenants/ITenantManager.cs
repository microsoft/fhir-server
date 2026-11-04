// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Tenants
{
    /// <summary>
    /// Provides the immutable startup registry of supported tenants.
    /// </summary>
    public interface ITenantManager
    {
        /// <summary>
        /// Gets the supported tenants keyed by case-sensitive tenant identifier.
        /// </summary>
        IReadOnlyDictionary<string, TenantInfo> Tenants { get; }
    }
}
