// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Scoping;
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
        public static IOwnedScope<T> CreateMockScope<T>(this T obj)
        {
            var scope = Substitute.For<IOwnedScope<T>>();
            scope.Value.Returns(obj);
            return scope;
        }

        /// <summary>
        /// Creates a substitute factory returning an existing handle.
        /// </summary>
        /// <typeparam name="T">The service type.</typeparam>
        /// <param name="obj">The handle returned by the factory.</param>
        /// <returns>A substitute scope factory.</returns>
        public static IOwnedScopeFactory<T> CreateMockScopeFactoryFromScope<T>(this IOwnedScope<T> obj)
        {
            IOwnedScopeFactory<T> provider = Substitute.For<IOwnedScopeFactory<T>>();
            provider.Create().Returns(obj);
            return provider;
        }

        /// <summary>
        /// Creates a substitute factory for a service.
        /// </summary>
        /// <typeparam name="T">The service type.</typeparam>
        /// <param name="obj">The service returned within each handle.</param>
        /// <returns>A substitute scope factory.</returns>
        public static IOwnedScopeFactory<T> CreateMockScopeFactory<T>(this T obj)
        {
            return obj
                .CreateMockScope()
                .CreateMockScopeFactoryFromScope();
        }

        /// <summary>
        /// Creates a substitute factory that resolves the service on each call.
        /// </summary>
        /// <typeparam name="T">The service type.</typeparam>
        /// <param name="obj">The service creation delegate.</param>
        /// <returns>A substitute scope factory.</returns>
        public static IOwnedScopeFactory<T> CreateMockScopeFactory<T>(this Func<T> obj)
        {
            IOwnedScopeFactory<T> provider = Substitute.For<IOwnedScopeFactory<T>>();
            provider.Create().Returns(_ => obj.Invoke().CreateMockScope());
            return provider;
        }
    }
}
