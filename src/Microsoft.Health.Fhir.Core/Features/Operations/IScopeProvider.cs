// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Scoping;

namespace Microsoft.Health.Fhir.Core.Features.Operations;

/// <summary>
/// Provides service handles owned and disposed by the caller, not by the container resolving the provider.
/// </summary>
/// <typeparam name="T">The service held by each handle.</typeparam>
public interface IScopeProvider<T>
{
    /// <summary>
    /// Acquires a service handle that the caller must keep alive until its work with the service completes.
    /// </summary>
    /// <returns>A caller-owned handle. Whether disposal releases the service depends on the implementation.</returns>
    IScoped<T> Invoke();
}
