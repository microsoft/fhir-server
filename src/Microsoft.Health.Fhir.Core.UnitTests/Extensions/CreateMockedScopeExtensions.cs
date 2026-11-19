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
        /// Creates a substitute handle that always returns the supplied value, even after disposal.
        /// </summary>
        /// <typeparam name="T">The service type.</typeparam>
        /// <param name="obj">The value returned by the handle; disposing the substitute does not dispose it.</param>
        /// <returns>A substitute handle with no real scope lifetime.</returns>
        public static IScoped<T> CreateMockScope<T>(this T obj)
        {
            var scope = Substitute.For<IScoped<T>>();
            scope.Value.Returns(obj);
            return scope;
        }

        /// <summary>
        /// Creates a substitute provider that returns the same supplied handle on every invocation.
        /// </summary>
        /// <typeparam name="T">The service type.</typeparam>
        /// <param name="obj">The handle reused by the provider.</param>
        /// <returns>A substitute provider that does not create independent scopes.</returns>
        public static IScopeProvider<T> CreateMockScopeProviderFromScoped<T>(this IScoped<T> obj)
        {
            IScopeProvider<T> provider = Substitute.For<IScopeProvider<T>>();
            provider.Invoke().Returns(obj);
            return provider;
        }

        /// <summary>
        /// Creates a substitute provider that reuses one substitute handle and the supplied value on every invocation.
        /// </summary>
        /// <typeparam name="T">The service type.</typeparam>
        /// <param name="obj">The value returned by the reused handle.</param>
        /// <returns>A substitute provider with no real scope lifetime.</returns>
        public static IScopeProvider<T> CreateMockScopeProvider<T>(this T obj)
        {
            return obj
                .CreateMockScope()
                .CreateMockScopeProviderFromScoped();
        }

        /// <summary>
        /// Invokes the delegate on each provider invocation and wraps its result in a new substitute handle.
        /// </summary>
        /// <typeparam name="T">The service type.</typeparam>
        /// <param name="obj">The delegate supplying each value; it may itself return a reused value.</param>
        /// <returns>A substitute provider whose handles do not dispose their values or create real DI scopes.</returns>
        public static IScopeProvider<T> CreateMockScopeProvider<T>(this Func<T> obj)
        {
            IScopeProvider<T> provider = Substitute.For<IScopeProvider<T>>();
            provider.Invoke().Returns(_ => obj.Invoke().CreateMockScope());
            return provider;
        }
    }
}
