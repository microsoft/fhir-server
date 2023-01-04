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
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// The custom <see cref="XunitTestFrameworkExecutor"/> that has special handling for test classes that use fixtures with parameterized constructor arguments.
    /// </summary>
    internal sealed class CustomXunitTestFrameworkExecutor : XunitTestFrameworkExecutor
    {
        public CustomXunitTestFrameworkExecutor(AssemblyName assemblyName, ISourceInformationProvider sourceInformationProvider, IMessageSink diagnosticMessageSink)
            : base(assemblyName, sourceInformationProvider, diagnosticMessageSink)
        {
            AssemblyInfo = new CustomAssemblyInfo(AssemblyInfo);
        }

        protected override void RunTestCases(IEnumerable<IXunitTestCase> testCases, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
        {
            using (var assemblyRunner = new AssemblyRunner(TestAssembly, testCases, DiagnosticMessageSink, executionMessageSink, executionOptions))
            {
                assemblyRunner.RunAsync().GetAwaiter().GetResult();
            }
        }

        private sealed class AssemblyRunner : XunitTestAssemblyRunner
        {
            private readonly Dictionary<Type, object> _assemblyFixtureMappings = new Dictionary<Type, object>();
            private ExecutionContext _context;

            public AssemblyRunner(ITestAssembly testAssembly, IEnumerable<IXunitTestCase> testCases, IMessageSink diagnosticMessageSink, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
                : base(testAssembly, testCases, diagnosticMessageSink, executionMessageSink, executionOptions)
            {
            }

            protected override async Task AfterTestAssemblyStartingAsync()
            {
                // Let everything initialize
                await base.AfterTestAssemblyStartingAsync();

                // Find the AssemblyFixtureAttributes placed on the test assembly
                Aggregator.Run(() =>
                {
                    var fixtureAttributes = ((IReflectionAssemblyInfo)TestAssembly.Assembly).Assembly
                        .GetCustomAttributes(typeof(AssemblyFixtureAttribute), false)
                        .Cast<AssemblyFixtureAttribute>();

                    foreach (var fixtureAttr in fixtureAttributes)
                    {
                        _assemblyFixtureMappings[fixtureAttr.FixtureType] = Activator.CreateInstance(fixtureAttr.FixtureType);
                    }
                });

                _context = ExecutionContext.Capture();
            }

            protected override async Task BeforeTestAssemblyFinishedAsync()
            {
                foreach (var fixture in _assemblyFixtureMappings.Values)
                {
                    switch (fixture)
                    {
                        case IAsyncLifetime d:
                            await d.DisposeAsync();
                            break;
                        case IAsyncDisposable d:
                            await d.DisposeAsync();
                            break;
                        case IDisposable d:
                            d.Dispose();
                            break;
                    }
                }

                await base.BeforeTestAssemblyFinishedAsync();
            }

            protected override Task<RunSummary> RunTestCollectionAsync(IMessageBus messageBus, ITestCollection testCollection, IEnumerable<IXunitTestCase> testCases, CancellationTokenSource cancellationTokenSource)
            {
                Task<RunSummary> result = null;
                ExecutionContext.Run(_context, state => result = new CollectionRunner(_assemblyFixtureMappings, testCollection, testCases, DiagnosticMessageSink, messageBus, TestCaseOrderer, new ExceptionAggregator(Aggregator), cancellationTokenSource).RunAsync(), state: null);
                return result;
            }

            public override void Dispose()
            {
                _context?.Dispose();
                base.Dispose();
            }
        }

        private sealed class CollectionRunner : XunitTestCollectionRunner
        {
            private readonly Dictionary<Type, object> _assemblyFixtureMappings;

            public CollectionRunner(Dictionary<Type, object> assemblyFixtureMappings, ITestCollection testCollection, IEnumerable<IXunitTestCase> testCases, IMessageSink diagnosticMessageSink, IMessageBus messageBus, ITestCaseOrderer testCaseOrderer, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource)
                : base(testCollection, testCases, diagnosticMessageSink, messageBus, testCaseOrderer, aggregator, cancellationTokenSource)
            {
                _assemblyFixtureMappings = assemblyFixtureMappings;
            }

            protected override Task<RunSummary> RunTestClassAsync(ITestClass testClass, IReflectionTypeInfo @class, IEnumerable<IXunitTestCase> testCases)
            {
                EnsureArg.IsNotNull(testClass, nameof(testClass));
                EnsureArg.IsNotNull(@class, nameof(@class));
                EnsureArg.IsNotNull(testCases, nameof(testCases));

                Dictionary<Type, object> combinedMappings = null;

                if (testClass.Class is TestClassWithFixtureArgumentsTypeInfo classWithFixtureArguments)
                {
                    combinedMappings = new Dictionary<Type, object>(CollectionFixtureMappings);

                    foreach (var variant in classWithFixtureArguments.FixtureArguments)
                    {
                        combinedMappings.Add(variant.EnumValue.GetType(), variant.EnumValue);
                    }
                }

                if (_assemblyFixtureMappings.Count > 0 && combinedMappings == null)
                {
                    combinedMappings = new Dictionary<Type, object>(CollectionFixtureMappings);
                }

                foreach (var assemblyFixtureMapping in _assemblyFixtureMappings)
                {
                    combinedMappings.Add(assemblyFixtureMapping.Key, assemblyFixtureMapping.Value);
                }

                return new ExecutionContextFlowingClassRunner(
                        testClass,
                        @class,
                        testCases,
                        DiagnosticMessageSink,
                        MessageBus,
                        TestCaseOrderer,
                        new ExceptionAggregator(Aggregator),
                        CancellationTokenSource,
                        combinedMappings ?? CollectionFixtureMappings)
                    .RunAsync();
            }
        }

        /// <summary>
        /// An <see cref="XunitTestClassRunner"/> that runs tests in the same <see cref="ExecutionContext"/> as when all fixture constructors ran.
        /// This means that <see cref="AsyncLocal{T}"/>s set during a fixture constructor can be read during test method execution.
        /// </summary>
        private sealed class ExecutionContextFlowingClassRunner : XunitTestClassRunner
        {
            private ExecutionContext _context;

            public ExecutionContextFlowingClassRunner(ITestClass testClass, IReflectionTypeInfo @class, IEnumerable<IXunitTestCase> testCases, IMessageSink diagnosticMessageSink, IMessageBus messageBus, ITestCaseOrderer testCaseOrderer, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource, IDictionary<Type, object> collectionFixtureMappings)
                : base(testClass, @class, testCases, diagnosticMessageSink, messageBus, testCaseOrderer, aggregator, cancellationTokenSource, collectionFixtureMappings)
            {
            }

            protected override void CreateClassFixture(Type fixtureType)
            {
                base.CreateClassFixture(fixtureType);
                _context = ExecutionContext.Capture();
            }

            protected override Task<RunSummary> RunTestMethodsAsync()
            {
                if (_context == null)
                {
                    // no class fixtures, so context needed

                    return base.RunTestMethodsAsync();
                }

                Task<RunSummary> result = null;
                ExecutionContext.Run(_context, state => result = base.RunTestMethodsAsync(), null);
                return result;
            }
        }

        /// <summary>
        /// <see cref="XunitTestCase"/> facts have special optimized serialization. Their deserialization needs to be able to instantiate
        /// the synthetic classes that we created of the form Namespace.Class(Arg1, Arg2). For these, we want to create a
        /// <see cref="TestClassWithFixtureArgumentsTypeInfo"/> with Arg1 and Arg2 as the fixture arguments
        /// </summary>
        private sealed class CustomAssemblyInfo : IAssemblyInfo
        {
            private readonly IAssemblyInfo _assemblyInfoImplementation;
            private readonly Regex _argumentsRegex = new Regex(@"\((\s*(?<VALUE>[^, )]+)\s*,?)*\)");

            public CustomAssemblyInfo(IAssemblyInfo assemblyInfoImplementation)
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

                SingleFlag[] arguments = attributeInfo
                    .GetConstructorArguments()
                    .Cast<Enum>()
                    .Zip(
                        match.Groups["VALUE"].Captures,
                        (e, c) => new SingleFlag((Enum)Enum.Parse(e.GetType(), c.Value)))
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
