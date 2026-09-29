// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Xunit.Sdk;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// The class runner that seeds each variant's <c>(DataStore, Format)</c> values into the collection-scoped fixture
    /// cache so xUnit can resolve fixture and test-class constructor arguments.
    /// </summary>
    internal sealed class CustomXunitTestClassRunner : XunitTestClassRunner
    {
        // Reflection point #3: the collection-scoped fixture cache we seed enum values into.
        private static readonly FieldInfo FixtureCacheField =
            typeof(FixtureMappingManager).GetField("fixtureCache", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("FixtureMappingManager.fixtureCache (private) was not found. The xunit.v3 3.2.2 pin has changed; fixture arguments cannot be injected.");

        // Reflection point #4: a class-level manager delegates to its parent (collection) manager, whose
        // cache is the one a class fixture is actually built from.
        private static readonly FieldInfo ParentMappingManagerField =
            typeof(FixtureMappingManager).GetField("parentMappingManager", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("FixtureMappingManager.parentMappingManager (private) was not found. The xunit.v3 3.2.2 pin has changed; fixture arguments cannot be injected.");

        protected override async ValueTask<bool> OnTestClassStarting(XunitTestClassRunnerContext context)
        {
            InjectFixtureArguments(context);
            return await base.OnTestClassStarting(context);
        }

        // Seeds DataStore/Format into the fixture cache so a class fixture ctor taking them resolves.
        private static void InjectFixtureArguments(XunitTestClassRunnerContext context)
        {
            if (context?.TestClass is not FixtureArgumentSetTestClass variantClass)
            {
                return;
            }

            FixtureMappingManager cacheOwner = context.ClassFixtureMappings;
            if (ParentMappingManagerField.GetValue(cacheOwner) is FixtureMappingManager parent)
            {
                cacheOwner = parent;
            }

            if (FixtureCacheField.GetValue(cacheOwner) is not IDictionary<Type, object> cache)
            {
                throw new InvalidOperationException("The xUnit fixture cache could not be read. The xunit.v3 3.2.2 pin has changed.");
            }

            // Overwrite, never add-if-missing: the cache is collection-scoped and a class's variants share a
            // collection, so leaving a prior variant's value in place would build the fixture for the wrong store
            // and still pass. Each variant stamps its own values over the last.
            foreach (SingleFlag flag in variantClass.Flags)
            {
                cache[flag.EnumValue.GetType()] = flag.EnumValue;
            }
        }
    }
}
