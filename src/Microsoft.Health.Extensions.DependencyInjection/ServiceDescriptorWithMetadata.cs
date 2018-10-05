// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Health.Extensions.DependencyInjection
{
    /// <summary>
    /// A <see cref="ServiceDescriptor"/> with an additional metadata property.
    /// </summary>
    public class ServiceDescriptorWithMetadata : ServiceDescriptor
    {
        public ServiceDescriptorWithMetadata(Type serviceType, Type implementationType, ServiceLifetime lifetime, object metadata)
            : base(serviceType, implementationType, lifetime)
        {
            Metadata = metadata;
        }

        public ServiceDescriptorWithMetadata(Type serviceType, object instance, object metadata)
            : base(serviceType, instance)
        {
            Metadata = metadata;
        }

        public ServiceDescriptorWithMetadata(Type serviceType, Func<IServiceProvider, object> factory, ServiceLifetime lifetime, object metadata)
            : base(serviceType, factory, lifetime)
        {
            Metadata = metadata;
        }

        public object Metadata { get; }
    }
}
