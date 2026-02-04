// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// The custom <see cref="XunitTestFrameworkExecutor"/> that has special handling for test classes that use fixtures with parameterized constructor arguments.
    /// </summary>
    internal sealed class CustomXunitTestFrameworkExecutor : XunitTestFrameworkExecutor
    {
        public CustomXunitTestFrameworkExecutor(Assembly assembly)
            : base(new FixtureArgumentSetTestAssembly(assembly))
        {
        }

        public override async ValueTask RunTestCases(IReadOnlyCollection<IXunitTestCase> testCases, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions, CancellationToken cancellationToken)
        {
            var runner = new FixtureArgumentSetAssemblyRunner();
            await runner.Run((IXunitTestAssembly)TestAssembly, testCases, executionMessageSink, executionOptions, cancellationToken);
        }

        private sealed class FixtureArgumentSetTestAssembly : XunitTestAssembly
        {
            private readonly Assembly _assembly;
            private IReadOnlyCollection<Type> _assemblyFixtureTypes;
            private static readonly FieldInfo AssemblyFixtureTypesField = typeof(XunitTestAssembly).GetField("assemblyFixtureTypes", BindingFlags.Instance | BindingFlags.NonPublic);

            public FixtureArgumentSetTestAssembly(Assembly assembly)
                : base(assembly, configFileName: null, assembly.GetName().Version, UniqueIDGenerator.ForAssembly(assembly.Location, null))
            {
                _assembly = assembly;
            }

#pragma warning disable CS0618 // Called by the de-serializer; should only be called by deriving classes for de-serialization purposes
            public FixtureArgumentSetTestAssembly()
            {
            }
#pragma warning restore CS0618

            public new IReadOnlyCollection<Type> AssemblyFixtureTypes
            {
                get
                {
                    if (_assemblyFixtureTypes == null)
                    {
#pragma warning disable CS0618 // AssemblyFixtureAttribute is obsolete; usage is required for assembly fixture discovery.
                        _assemblyFixtureTypes = _assembly
                            .GetCustomAttributes(typeof(global::Xunit.AssemblyFixtureAttribute), inherit: false)
                            .Cast<global::Xunit.AssemblyFixtureAttribute>()
                            .Select(attribute => attribute.AssemblyFixtureType)
                            .ToArray();
#pragma warning restore CS0618

                        AssemblyFixtureTypesField?.SetValue(this, new Lazy<IReadOnlyCollection<Type>>(() => _assemblyFixtureTypes));
                    }

                    return _assemblyFixtureTypes;
                }
            }
        }

        private sealed class FixtureArgumentSetAssemblyRunner : XunitTestAssemblyRunner
        {
            protected override async ValueTask<RunSummary> RunTestCollection(XunitTestAssemblyRunnerContext context, IXunitTestCollection testCollection, IReadOnlyCollection<IXunitTestCase> testCases)
            {
                var testCaseOrderer = context.AssemblyTestCaseOrderer ?? DefaultTestCaseOrderer.Instance;
                var runner = new FixtureArgumentSetCollectionRunner();
                var summary = await runner.Run(testCollection, testCases, context.ExplicitOption, context.MessageBus, testCaseOrderer, context.Aggregator, context.CancellationTokenSource, context.AssemblyFixtureMappings);
                return summary;
            }
        }

        private sealed class FixtureArgumentSetCollectionRunner : XunitTestCollectionRunner
        {
            protected override async ValueTask<RunSummary> RunTestClass(XunitTestCollectionRunnerContext context, IXunitTestClass testClass, IReadOnlyCollection<IXunitTestCase> testCases)
            {
                var testCaseOrderer = context.TestCaseOrderer ?? DefaultTestCaseOrderer.Instance;
                var classRunner = new FixtureArgumentSetClassRunner();
                var summary = await classRunner.Run(testClass, testCases, context.ExplicitOption, context.MessageBus, testCaseOrderer, context.Aggregator, context.CancellationTokenSource, context.CollectionFixtureMappings);
                return summary;
            }
        }

        private sealed class FixtureArgumentSetClassRunner : XunitTestClassRunner
        {
            private static readonly FieldInfo FixtureCacheField = typeof(FixtureMappingManager)
                .GetField("fixtureCache", BindingFlags.Instance | BindingFlags.NonPublic);

            protected override ValueTask<object[]> CreateTestClassConstructorArguments(XunitTestClassRunnerContext context)
            {
                InjectFixtureArguments(context);
                return base.CreateTestClassConstructorArguments(context);
            }

            private static void InjectFixtureArguments(XunitTestClassRunnerContext context)
            {
                if (context?.TestClass is not FixtureArgumentSetTestClass fixtureTestClass)
                {
                    return;
                }

                if (FixtureCacheField == null)
                {
                    return;
                }

                var cache = FixtureCacheField.GetValue(context.ClassFixtureMappings) as IDictionary<Type, object>;
                if (cache == null)
                {
                    return;
                }

                var fixtureArguments = fixtureTestClass.GetFixtureArguments();
                if (fixtureArguments.Count == 0)
                {
                    return;
                }

                foreach (var argument in fixtureArguments)
                {
                    var argumentType = argument.EnumValue.GetType();
                    if (!cache.ContainsKey(argumentType))
                    {
                        cache[argumentType] = argument.EnumValue;
                    }
                }
            }
        }
    }
}
