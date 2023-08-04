// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
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

        public static Func<IScoped<T>> CreateMockScopeFactory<T>(this T obj)
        {
            return () => obj.CreateMockScope();
        }

        public static IScopeProvider<T> CreateMockScopeProviderFromScoped<T>(this IScoped<T> obj)
        {
            IScopeProvider<T> provider = Substitute.For<IScopeProvider<T>>();
            provider.Invoke().Returns(obj);
            return provider;
        }

        public static IScopeProvider<T> CreateMockScopeProvider<T>(this T obj)
        {
            if (obj.GetType().IsGenericType && obj.GetType().GetGenericTypeDefinition() == typeof(IScoped<>))
            {
                throw new InvalidOperationException($"Unwrap {typeof(T)} or use {nameof(CreateMockScopeProviderFromScoped)}.");
            }

            return obj
                .CreateMockScope()
                .CreateMockScopeProviderFromScoped();
        }

        public static IScopeProvider<T> CreateMockScopeProvider<T>(this Func<T> obj)
        {
            if (obj.GetType().IsGenericType && obj.GetType().GetGenericTypeDefinition() == typeof(IScoped<>))
            {
                throw new InvalidOperationException($"Unwrap {typeof(T)} or use {nameof(CreateMockScopeProviderFromScoped)}.");
            }

            IScopeProvider<T> provider = Substitute.For<IScopeProvider<T>>();
            provider.Invoke().Returns(_ => obj.Invoke().CreateMockScope());
            return provider;
        }
    }
}
