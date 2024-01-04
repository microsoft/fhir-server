// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Extensions.DependencyInjection;
using NSubstitute;

namespace Microsoft.Health.Fhir.Core.UnitTests
{
    public class SubstituteHelpers
    {
        public static Func<IScoped<T>> ScopedFunc<T>(T obj)
        {
            var scope = Substitute.For<IScoped<T>>();
            scope.Value.Returns(obj);
            return () => scope;
        }
    }
}
