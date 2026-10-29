// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Scoping;

/// <summary>
/// Creates scopes owned and disposed by the caller rather than the resolving container.
/// </summary>
/// <typeparam name="T">The service resolved within each scope.</typeparam>
public interface IOwnedScopeFactory<out T>
{
    /// <summary>
    /// Creates an independent scope whose lifetime belongs to the caller.
    /// </summary>
    /// <returns>A caller-owned service handle.</returns>
    IOwnedScope<T> Create();
}
