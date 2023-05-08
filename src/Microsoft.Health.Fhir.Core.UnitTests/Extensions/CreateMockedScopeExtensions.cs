// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Operations;
using NSubstitute;

namespace Microsoft.Health.Fhir.Core.UnitTests.Extensions
{
    public static class CreateMockedScopeExtensions
    {
        public static IScoped<T> CreateMockScope<T>(this T obj)
        {
            var scope = Substitute.For<IScoped<T>>();
            scope.Value.Returns(obj);
            return scope;
        }

        public static IBackgroundScopeProvider<T> CreateMockBackgroundScopeProviderFromScoped<T>(this IScoped<T> obj)
        {
            IBackgroundScopeProvider<T> provider = Substitute.For<IBackgroundScopeProvider<T>>();
            provider.Invoke().Returns(obj);
            return provider;
        }

        public static IBackgroundScopeProvider<T> CreateMockBackgroundScopeProvider<T>(this T obj)
        {
            if (obj.GetType().IsGenericType && obj.GetType().GetGenericTypeDefinition() == typeof(IScoped<>))
            {
                throw new InvalidOperationException($"Unwrap {typeof(T)} or use {nameof(CreateMockBackgroundScopeProviderFromScoped)}.");
            }

            return obj
                .CreateMockScope()
                .CreateMockBackgroundScopeProviderFromScoped();
        }
    }
}
