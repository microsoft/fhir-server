// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Health.Extensions.DependencyInjection
{
    public static class ServiceDescriptorExtensions
    {
        /// <summary>
        /// Creates a <see cref="ServiceDescriptorWithMetadata"/> from a given <paramref name="descriptor"/> and <paramref name="metadata"/> arguments.
        /// </summary>
        /// <param name="descriptor">An existing <see cref="ServiceDescriptor"/>.</param>
        /// <param name="metadata">Metadata to attach to the service descriptor</param>
        /// <returns>A new <see cref="ServiceDescriptorWithMetadata"/>.</returns>
        public static ServiceDescriptorWithMetadata WithMetadata(this ServiceDescriptor descriptor, object metadata)
        {
            EnsureArg.IsNotNull(value: descriptor, paramName: nameof(descriptor));

            if (descriptor.ImplementationInstance != null)
            {
                return new ServiceDescriptorWithMetadata(serviceType: descriptor.ServiceType, instance: descriptor.ImplementationInstance, metadata: metadata);
            }

            if (descriptor.ImplementationType != null)
            {
                return new ServiceDescriptorWithMetadata(serviceType: descriptor.ServiceType, implementationType: descriptor.ImplementationType, lifetime: descriptor.Lifetime, metadata: metadata);
            }

            return new ServiceDescriptorWithMetadata(serviceType: descriptor.ServiceType, factory: descriptor.ImplementationFactory, lifetime: descriptor.Lifetime, metadata: metadata);
        }
    }
}
