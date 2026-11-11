// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit.Sdk;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// The class runner that supplies each variant's fixture arguments to xUnit.
    /// </summary>
    internal sealed class CustomXunitTestClassRunner : XunitTestClassRunner
    {
        private static readonly FieldInfo FixtureCacheField =
            typeof(FixtureMappingManager).GetField("fixtureCache", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("FixtureMappingManager.fixtureCache (private) was not found; fixture arguments cannot be injected.");

        public static FixtureMappingManager CreateFixtureArguments(FixtureArgumentSetTestClass variantClass, FixtureMappingManager parent)
        {
            var arguments = new FixtureMappingManager("Fixture arguments", parent);
            if (FixtureCacheField.GetValue(arguments) is not IDictionary<Type, object> cache)
            {
                throw new InvalidOperationException("The xUnit fixture cache could not be read; fixture arguments cannot be injected.");
            }

            foreach (SingleFlag flag in variantClass.Flags)
            {
                cache[flag.EnumValue.GetType()] = flag.EnumValue;
            }

            return arguments;
        }
    }
}
