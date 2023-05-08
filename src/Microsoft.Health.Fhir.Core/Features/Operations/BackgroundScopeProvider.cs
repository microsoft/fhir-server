// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.Core.Features.Operations;

/// <summary>
/// Provides a factory to resolve instances in a background task.
/// This class should not implement IDisposable, this is to avoid being referenced by the root IoC container.
/// </summary>
/// <typeparam name="T">The service type to resolve.</typeparam>
public class BackgroundScopeProvider<T> : IBackgroundScopeProvider<T>
{
    private readonly IServiceProvider _serviceProvider;

    public BackgroundScopeProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = EnsureArg.IsNotNull(serviceProvider, nameof(serviceProvider));
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Scope is disposed in returned class.")]
    public IScoped<T> Invoke()
    {
        return new BackgroundOperation<T>(_serviceProvider.CreateScope());
    }
}
