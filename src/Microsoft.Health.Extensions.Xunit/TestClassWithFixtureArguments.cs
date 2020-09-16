// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EnsureThat;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// A <see cref="ITestClass"/> where the class' <see cref="ITypeInfo"/> is a <see cref="TestClassWithFixtureArgumentsTypeInfo"/>.
    /// This test class uses a fixture with a single set of constructor arguments.
    /// </summary>
    public class TestClassWithFixtureArguments : LongLivedMarshalByRefObject, ITestClass
    {
        private ITypeInfo _underlyingClass;

        [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
        public TestClassWithFixtureArguments()
        {
        }

        public TestClassWithFixtureArguments(ITestCollection testCollection, ITypeInfo underlyingClass, SingleFlagEnum[] fixtureArguments)
        {
            EnsureArg.IsNotNull(testCollection, nameof(testCollection));
            EnsureArg.IsNotNull(underlyingClass, nameof(underlyingClass));
            EnsureArg.IsNotNull(fixtureArguments, nameof(fixtureArguments));

            _underlyingClass = underlyingClass;
            Class = new TestClassWithFixtureArgumentsTypeInfo(underlyingClass, fixtureArguments);
            FixtureArguments = fixtureArguments;
            TestCollection = testCollection;
        }

        /// <inheritdoc/>
        public ITypeInfo Class { get; set; }

        public IReadOnlyList<SingleFlagEnum> FixtureArguments { get; set; }

        /// <inheritdoc/>
        public ITestCollection TestCollection { get; set; }

        /// <inheritdoc/>
        public void Serialize(IXunitSerializationInfo info)
        {
            info.AddValue("TestCollection", TestCollection);
            info.AddValue("ClassAssemblyName", _underlyingClass.Assembly.Name);
            info.AddValue("ClassTypeName", _underlyingClass.Name);
            info.AddValue("FixtureArguments", FixtureArguments.Select(a => a.EnumValue).ToArray());
        }

        /// <inheritdoc/>
        public void Deserialize(IXunitSerializationInfo info)
        {
            TestCollection = info.GetValue<ITestCollection>("TestCollection");

            var assemblyName = info.GetValue<string>("ClassAssemblyName");
            var typeName = info.GetValue<string>("ClassTypeName");

            _underlyingClass = Reflector.Wrap(GetType(assemblyName, typeName));

            Enum[] rawFixtureArguments = info.GetValue<Enum[]>("FixtureArguments");

            FixtureArguments = Array.ConvertAll(rawFixtureArguments, e => new SingleFlagEnum(e));

            Class = new TestClassWithFixtureArgumentsTypeInfo(_underlyingClass, FixtureArguments);
        }

        /// <summary>
        /// Converts an assembly name + type name into a <see cref="Type"/> object.
        /// </summary>
        /// <param name="assemblyName">The assembly name.</param>
        /// <param name="typeName">The type name.</param>
        /// <returns>The instance of the <see cref="Type"/>, if available; <c>null</c>, otherwise.</returns>
        public static Type GetType(string assemblyName, string typeName)
        {
            // Make sure we only use the short form
            var an = new AssemblyName(assemblyName);
            var assembly = Assembly.Load(new AssemblyName { Name = an.Name, Version = an.Version });

            return assembly == null ? null : assembly.GetType(typeName);
        }
    }
}
