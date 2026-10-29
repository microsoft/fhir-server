// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.Core.Features.Scoping;

/// <summary>
/// Creates caller-owned scopes for resolving services.
/// This factory intentionally does not implement IDisposable so the resolving container or scope does not retain it for disposal.
/// </summary>
/// <typeparam name="T">The service type to resolve.</typeparam>
public class OwnedScopeFactory<T> : IOwnedScopeFactory<T>
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="OwnedScopeFactory{T}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The provider used to create independent child scopes.</param>
    public OwnedScopeFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = EnsureArg.IsNotNull(serviceProvider, nameof(serviceProvider));
    }

    /// <inheritdoc />
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Scope is disposed in returned class.")]
    public IOwnedScope<T> Create()
    {
        IServiceScope scope = _serviceProvider.CreateScope();
        try
        {
            return new ScopedComponent<T>(scope);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
