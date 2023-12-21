// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// A special <see cref="ITypeInfo"/> for a test class that will use a fixture instantiated with with a single set of constructor arguments.
    /// The <see cref="Name"/> property is customized to be of the form Namespace.Class(Arg1, Arg2)
    /// </summary>
    public class TestClassWithFixtureArgumentsTypeInfo : IReflectionTypeInfo
    {
        private readonly ITypeInfo _typeInfoImplementation;

        public TestClassWithFixtureArgumentsTypeInfo(ITypeInfo typeInfoImplementation, IReadOnlyList<SingleFlag> fixtureArguments)
        {
            EnsureArg.IsNotNull(typeInfoImplementation, nameof(typeInfoImplementation));
            EnsureArg.IsNotNull(fixtureArguments, nameof(fixtureArguments));

            _typeInfoImplementation = typeInfoImplementation;
            FixtureArguments = fixtureArguments;
            Name = $"{typeInfoImplementation.Name}[{string.Join(", ", fixtureArguments.Select(v => $"{v.EnumValue}"))}]";
        }

        public IReadOnlyList<SingleFlag> FixtureArguments { get; }

        public IAssemblyInfo Assembly => _typeInfoImplementation.Assembly;

        public ITypeInfo BaseType => _typeInfoImplementation.BaseType;

        public IEnumerable<ITypeInfo> Interfaces => _typeInfoImplementation.Interfaces;

        public bool IsAbstract => _typeInfoImplementation.IsAbstract;

        public bool IsGenericParameter => _typeInfoImplementation.IsGenericParameter;

        public bool IsGenericType => _typeInfoImplementation.IsGenericType;

        public bool IsSealed => _typeInfoImplementation.IsSealed;

        public bool IsValueType => _typeInfoImplementation.IsValueType;

        public string Name { get; }

        public Type Type => ((ReflectionTypeInfo)_typeInfoImplementation).Type;

        public IEnumerable<IAttributeInfo> GetCustomAttributes(string assemblyQualifiedAttributeTypeName)
        {
            return _typeInfoImplementation.GetCustomAttributes(assemblyQualifiedAttributeTypeName);
        }

        public IEnumerable<ITypeInfo> GetGenericArguments()
        {
            return _typeInfoImplementation.GetGenericArguments();
        }

        public IMethodInfo GetMethod(string methodName, bool includePrivateMethod)
        {
            return _typeInfoImplementation.GetMethod(methodName, includePrivateMethod);
        }

        public IEnumerable<IMethodInfo> GetMethods(bool includePrivateMethods)
        {
            return _typeInfoImplementation.GetMethods(includePrivateMethods);
        }
    }
}
