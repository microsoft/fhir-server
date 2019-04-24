// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// The custom <see cref="XunitTestFrameworkExecutor"/> that has special handling for test classes that use fixtures with parameterized constructor arguments.
    /// </summary>
    public class FixtureArgumentSetsXunitTestFrameworkExecutor : XunitTestFrameworkExecutor
    {
        public FixtureArgumentSetsXunitTestFrameworkExecutor(AssemblyName assemblyName, ISourceInformationProvider sourceInformationProvider, IMessageSink diagnosticMessageSink)
            : base(assemblyName, sourceInformationProvider, diagnosticMessageSink)
        {
            AssemblyInfo = new FixtureArgumentsAssemblyInfo(AssemblyInfo);
        }

        protected override void RunTestCases(IEnumerable<IXunitTestCase> testCases, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
        {
            using (var assemblyRunner = new AssemblyRunner(TestAssembly, testCases, DiagnosticMessageSink, executionMessageSink, executionOptions))
            {
                assemblyRunner.RunAsync().GetAwaiter().GetResult();
            }
        }

        private class AssemblyRunner : XunitTestAssemblyRunner
        {
            public AssemblyRunner(ITestAssembly testAssembly, IEnumerable<IXunitTestCase> testCases, IMessageSink diagnosticMessageSink, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
                : base(testAssembly, testCases, diagnosticMessageSink, executionMessageSink, executionOptions)
            {
            }

            protected override Task<RunSummary> RunTestCollectionAsync(IMessageBus messageBus, ITestCollection testCollection, IEnumerable<IXunitTestCase> testCases, CancellationTokenSource cancellationTokenSource)
                => new CollectionRunner(testCollection, testCases, DiagnosticMessageSink, messageBus, TestCaseOrderer, new ExceptionAggregator(Aggregator), cancellationTokenSource).RunAsync();
        }

        private class CollectionRunner : XunitTestCollectionRunner
        {
            public CollectionRunner(ITestCollection testCollection, IEnumerable<IXunitTestCase> testCases, IMessageSink diagnosticMessageSink, IMessageBus messageBus, ITestCaseOrderer testCaseOrderer, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource)
                : base(testCollection, testCases, diagnosticMessageSink, messageBus, testCaseOrderer, aggregator, cancellationTokenSource)
            {
            }

            protected override Task<RunSummary> RunTestClassAsync(ITestClass testClass, IReflectionTypeInfo @class, IEnumerable<IXunitTestCase> testCases)
            {
                EnsureArg.IsNotNull(testClass, nameof(testClass));
                EnsureArg.IsNotNull(@class, nameof(@class));
                EnsureArg.IsNotNull(testCases, nameof(testCases));

                if (!(testClass.Class is TestClassWithFixtureArgumentsTypeInfo classWithFixtureArguments))
                {
                    return base.RunTestClassAsync(testClass, @class, testCases);
                }

                // this is a test class that needs special logic for instantiating its fixture.

                var combinedMappings = new Dictionary<Type, object>(CollectionFixtureMappings);
                foreach (var variant in classWithFixtureArguments.FixtureArguments)
                {
                    combinedMappings.Add(variant.EnumValue.GetType(), variant.EnumValue);
                }

                return new XunitTestClassRunner(
                        testClass,
                        @class,
                        testCases,
                        DiagnosticMessageSink,
                        MessageBus,
                        TestCaseOrderer,
                        new ExceptionAggregator(Aggregator),
                        CancellationTokenSource,
                        combinedMappings)
                    .RunAsync();
            }
        }

        /// <summary>
        /// <see cref="XunitTestCase"/> facts have special optimized serialization. Their deserialization needs to be able to instantiate
        /// the synthetic classes that we created of the form Namespace.Class(Arg1, Arg2). For these, we want to create a
        /// <see cref="TestClassWithFixtureArgumentsTypeInfo"/> with Arg1 and Arg2 as the fixture arguments
        /// </summary>
        private class FixtureArgumentsAssemblyInfo : IAssemblyInfo
        {
            private readonly IAssemblyInfo _assemblyInfoImplementation;
            private readonly Regex _argumentsRegex = new Regex(@"\((\s*(?<VALUE>[^, )]+)\s*,?)*\)");

            public FixtureArgumentsAssemblyInfo(IAssemblyInfo assemblyInfoImplementation)
            {
                _assemblyInfoImplementation = assemblyInfoImplementation;
            }

            public string AssemblyPath => _assemblyInfoImplementation.AssemblyPath;

            public string Name => _assemblyInfoImplementation.Name;

            public IEnumerable<IAttributeInfo> GetCustomAttributes(string assemblyQualifiedAttributeTypeName)
            {
                return _assemblyInfoImplementation.GetCustomAttributes(assemblyQualifiedAttributeTypeName);
            }

            public ITypeInfo GetType(string typeName)
            {
                EnsureArg.IsNotNull(typeName, nameof(typeName));

                // parse out the (Arg1, Arg2)
                var match = _argumentsRegex.Match(typeName);
                if (!match.Success)
                {
                    return _assemblyInfoImplementation.GetType(typeName);
                }

                // retrieve the real type
                var typeInfo = _assemblyInfoImplementation.GetType(typeName.Substring(0, match.Index));
                Debug.Assert(typeInfo != null, $"Could not find type {typeName} in assembly");

                // now get the the arguments. We don't know what type they are, so we look at the FixtureArgumentSetsAttribute on the class and look at its arguments
                IAttributeInfo attributeInfo = typeInfo.GetCustomAttributes(typeof(FixtureArgumentSetsAttribute)).Single();

                SingleFlagEnum[] arguments = attributeInfo
                    .GetConstructorArguments()
                    .Cast<Enum>()
                    .Zip(
                        match.Groups["VALUE"].Captures,
                        (e, c) => new SingleFlagEnum((Enum)Enum.Parse(e.GetType(), c.Value)))
                    .ToArray();

                return new TestClassWithFixtureArgumentsTypeInfo(typeInfo, arguments);
            }

            public IEnumerable<ITypeInfo> GetTypes(bool includePrivateTypes)
            {
                return _assemblyInfoImplementation.GetTypes(includePrivateTypes);
            }
        }
    }
}
