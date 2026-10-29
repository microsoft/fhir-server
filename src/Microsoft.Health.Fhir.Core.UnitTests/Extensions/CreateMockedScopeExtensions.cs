// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Scoping;
using NSubstitute;

namespace Microsoft.Health.Fhir.Core.UnitTests.Extensions
{
    public static class CreateMockedScopeExtensions
    {
        /// <summary>
        /// Creates a substitute handle for a service.
        /// </summary>
        /// <typeparam name="T">The service type.</typeparam>
        /// <param name="obj">The service returned by the handle.</param>
        /// <returns>A substitute caller-owned handle.</returns>
        public static IScoped<T> CreateMockScope<T>(this T obj)
        {
            var scope = Substitute.For<IScoped<T>>();
            scope.Value.Returns(obj);
            return scope;
        }

        /// <summary>
        /// Creates a substitute factory returning an existing handle.
        /// </summary>
        /// <typeparam name="T">The service type.</typeparam>
        /// <param name="obj">The handle returned by the factory.</param>
        /// <returns>A substitute scope factory.</returns>
        public static IScopeProvider<T> CreateMockScopeProviderFromScoped<T>(this IScoped<T> obj)
        {
            IScopeProvider<T> provider = Substitute.For<IScopeProvider<T>>();
            provider.Invoke().Returns(obj);
            return provider;
        }

        /// <summary>
        /// Creates a substitute factory for a service.
        /// </summary>
        /// <typeparam name="T">The service type.</typeparam>
        /// <param name="obj">The service returned within each handle.</param>
        /// <returns>A substitute scope factory.</returns>
        public static IScopeProvider<T> CreateMockScopeProvider<T>(this T obj)
        {
            return obj
                .CreateMockScope()
                .CreateMockScopeProviderFromScoped();
        }

        /// <summary>
        /// Creates a substitute factory that resolves the service on each call.
        /// </summary>
        /// <typeparam name="T">The service type.</typeparam>
        /// <param name="obj">The service creation delegate.</param>
        /// <returns>A substitute scope factory.</returns>
        public static IScopeProvider<T> CreateMockScopeProvider<T>(this Func<T> obj)
        {
            IScopeProvider<T> provider = Substitute.For<IScopeProvider<T>>();
            provider.Invoke().Returns(_ => obj.Invoke().CreateMockScope());
            return provider;
        }
    }
}
