// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.Core.Features;

public sealed class BackgroundOperation<T> : IScoped<T>
{
    private bool isDisposed = false;
    private IServiceScope _serviceScope;
    private readonly T _value;

    public BackgroundOperation(IServiceScope serviceScope)
    {
        _serviceScope = EnsureArg.IsNotNull(serviceScope, nameof(serviceScope));
        _value = _serviceScope.ServiceProvider.GetService<T>();
    }

    public T Value
    {
        get
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException($"Service scope for {typeof(T)} has been disposed.");
            }

            return _value;
        }
    }

    public void Dispose()
    {
        _serviceScope?.Dispose();
        _serviceScope = null;
        isDisposed = true;
    }
}
