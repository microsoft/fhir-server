// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Extensions.DependencyInjection;
using NSubstitute;

namespace Microsoft.Health.Fhir.Core.UnitTests.Extensions
{
    public static class CreateMockedScopeExtensions
    {
        public static IScoped<T> CreateMockScope<T>(this T obj)
        {
            var scope = Substitute.For<IScoped<T>>();
            scope.Value.Returns<T>(obj);
            return scope;
        }

        public static Func<IScoped<T>> CreateMockScopeFactory<T>(this T obj)
        {
            return () => obj.CreateMockScope();
        }
    }
}
