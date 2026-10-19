// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Core.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a factory whose returned scopes are owned by the caller rather than the resolving container.
    /// </summary>
    /// <typeparam name="T">The service resolved within each scope.</typeparam>
    /// <param name="serviceCollection">The service collection.</param>
    /// <returns>The collection with the scoped factory registered.</returns>
    public static IServiceCollection AddScopedFactory<T>(this IServiceCollection serviceCollection)
    {
        EnsureArg.IsNotNull(serviceCollection, nameof(serviceCollection));

        serviceCollection.TryAddTransient(typeof(IScopeProvider<>), typeof(ScopeProvider<>));
        serviceCollection.AddTransient<Func<IScoped<T>>>(provider => provider.GetRequiredService<IScopeProvider<T>>().Invoke);

        return serviceCollection;
    }

    public static IServiceCollection RemoveServiceTypeExact<TConcreteClass, TServiceType>(this IServiceCollection serviceCollection)
    {
        EnsureArg.IsNotNull(serviceCollection, nameof(serviceCollection));

        Type type = typeof(TConcreteClass);

        for (int i = serviceCollection.Count - 1; i >= 0; i--)
        {
            ServiceDescriptor descriptor = serviceCollection[i];
            if (descriptor.ServiceType == typeof(TServiceType) && descriptor.ImplementationType == type)
            {
                serviceCollection.RemoveAt(i);
            }
        }

        return serviceCollection;
    }
}
