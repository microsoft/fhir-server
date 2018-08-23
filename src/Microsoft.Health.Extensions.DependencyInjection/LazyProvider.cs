// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Health.Extensions.DependencyInjection
{
    /// <summary>
    /// Enables generic resolution of Lazy services from IoC
    /// </summary>
    /// <typeparam name="T">Type of service to resolve</typeparam>
    /// <seealso cref="System.Lazy{T}" />
    public class LazyProvider<T> : Lazy<T>
    {
        public LazyProvider(IServiceProvider serviceProvider)
            : base(() => IsNotNull(serviceProvider).GetService<T>())
        {
        }

        private static IServiceProvider IsNotNull(IServiceProvider serviceProvider)
        {
            EnsureArg.IsNotNull(serviceProvider, nameof(serviceProvider));

            return serviceProvider;
        }
    }
}
