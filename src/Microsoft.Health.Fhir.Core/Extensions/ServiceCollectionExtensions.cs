// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.Core.Extensions;

public static class ServiceCollectionExtensions
{
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
