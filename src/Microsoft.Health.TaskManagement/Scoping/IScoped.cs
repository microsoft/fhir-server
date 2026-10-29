// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Scoping;

/// <summary>
/// A service handle owned and disposed by its caller, never by the container resolving its factory.
/// </summary>
/// <typeparam name="T">The service held by the scope.</typeparam>
public interface IScoped<out T> : IDisposable
{
    /// <summary>
    /// Gets the service held by this scope.
    /// </summary>
    T Value { get; }
}
