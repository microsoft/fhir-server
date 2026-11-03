// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Scoping;

namespace Microsoft.Health.Fhir.Core.Features.Operations;

/// <summary>
/// Resolves services in independent child DI scopes owned by the caller.
/// This factory is not disposable, so DI does not retain it for disposal.
/// </summary>
/// <typeparam name="T">The service type to resolve.</typeparam>
public class ScopeProvider<T> : IScopeProvider<T>
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScopeProvider{T}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The provider used to create each independent child scope.</param>
    public ScopeProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = EnsureArg.IsNotNull(serviceProvider, nameof(serviceProvider));
    }

    /// <summary>
    /// Creates a child scope and resolves the required service, disposing the scope if resolution fails.
    /// </summary>
    /// <returns>
    /// A caller-owned handle that disposes the child scope and rejects <see cref="IScoped{T}.Value"/> access after disposal.
    /// </returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Scope is disposed in returned class.")]
    public IScoped<T> Invoke()
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
