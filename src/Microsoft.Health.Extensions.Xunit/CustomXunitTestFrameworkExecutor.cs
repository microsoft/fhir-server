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

            private static readonly FieldInfo ParentMappingManagerField = typeof(FixtureMappingManager)
                .GetField("parentMappingManager", BindingFlags.Instance | BindingFlags.NonPublic);

            protected override ValueTask<bool> OnTestClassStarting(XunitTestClassRunnerContext context)
            {
                InjectFixtureArguments(context);
                return base.OnTestClassStarting(context);
            }

            protected override ValueTask<object> GetConstructorArgument(XunitTestClassRunnerContext context, ConstructorInfo constructor, int index, ParameterInfo parameter)
            {
                if (context?.TestClass is FixtureArgumentSetTestClass fixtureTestClass)
                {
                    var fixtureArguments = fixtureTestClass.GetFixtureArguments();
                    if (fixtureArguments.Count > 0)
                    {
                        foreach (var argument in fixtureArguments)
                        {
                            var enumValue = argument.EnumValue;
                            if (enumValue != null && parameter.ParameterType == enumValue.GetType())
                            {
                                return new ValueTask<object>(enumValue);
                            }
                        }
                    }
                }

                return base.GetConstructorArgument(context, constructor, index, parameter);
            }

            protected override ValueTask<object[]> CreateTestClassConstructorArguments(XunitTestClassRunnerContext context)
            {
                return base.CreateTestClassConstructorArguments(context);
            }

            private static void InjectFixtureArguments(XunitTestClassRunnerContext context)
            {
                if (context?.TestClass == null)
                {
                    return;
                }

                if (FixtureCacheField == null)
                {
                    return;
                }

                var cacheOwner = context.ClassFixtureMappings;
                if (ParentMappingManagerField?.GetValue(cacheOwner) is FixtureMappingManager parentMappingManager)
                {
                    cacheOwner = parentMappingManager;
                }

                var cache = FixtureCacheField.GetValue(cacheOwner) as IDictionary<Type, object>;
                if (cache == null)
                {
                    return;
                }

                var fixtureArguments = new List<Enum>();
                if (context.TestClass is FixtureArgumentSetTestClass fixtureTestClass)
                {
                    fixtureArguments.AddRange(fixtureTestClass.GetFixtureArguments().Select(argument => argument.EnumValue));
                }

                var fixtureParameterTypes = new HashSet<Type>();
                var classFixtureTypes = context.TestClass.ClassFixtureTypes;
                if (classFixtureTypes != null)
                {
                    foreach (var fixtureType in classFixtureTypes)
                    {
                        var constructor = fixtureType.GetConstructors()
                            .SingleOrDefault(ctor => !ctor.IsStatic && ctor.IsPublic);
                        if (constructor == null)
                        {
                            continue;
                        }

                        foreach (var parameter in constructor.GetParameters())
                        {
                            if (parameter.ParameterType.IsEnum)
                            {
                                fixtureParameterTypes.Add(parameter.ParameterType);
                            }
                        }
                    }
                }

                var resolvedArguments = new Dictionary<Type, object>();

                foreach (var enumValue in fixtureArguments)
                {
                    var argumentType = enumValue.GetType();

                    foreach (var parameterType in fixtureParameterTypes)
                    {
                        if (string.Equals(parameterType.FullName, argumentType.FullName, StringComparison.Ordinal)
                            && !resolvedArguments.ContainsKey(parameterType))
                        {
                            resolvedArguments[parameterType] = parameterType == argumentType
                                ? enumValue
                                : Enum.ToObject(parameterType, Convert.ToInt64(enumValue));
                        }
                    }
                }

                if (resolvedArguments.Count == 0 && fixtureParameterTypes.Count > 0)
                {
                    var traits = GetTraits(context.TestCases);
                    foreach (var parameterType in fixtureParameterTypes)
                    {
                        var traitKey = parameterType.Name;
                        if (!traits.TryGetValue(traitKey, out var values) || values.Count == 0)
                        {
                            continue;
                        }

                        if (values.Count > 1)
                        {
                            throw new TestPipelineException($"Fixture argument '{traitKey}' had multiple values: {string.Join(", ", values)}");
                        }

                        var value = values.First();
                        if (string.IsNullOrWhiteSpace(value))
                        {
                            continue;
                        }

                        if (!Enum.TryParse(parameterType, value, ignoreCase: true, out var parsedValue))
                        {
                            throw new TestPipelineException($"Fixture argument '{traitKey}' value '{value}' could not be parsed as {parameterType.FullName}.");
                        }

                        resolvedArguments[parameterType] = parsedValue;
                    }
                }

                foreach (var resolvedArgument in resolvedArguments)
                {
                    if (!cache.ContainsKey(resolvedArgument.Key))
                    {
                        cache[resolvedArgument.Key] = resolvedArgument.Value;
                    }
                }
            }

            private static Dictionary<string, IReadOnlyCollection<string>> GetTraits(IReadOnlyCollection<IXunitTestCase> testCases)
            {
                var traits = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

                foreach (var testCase in testCases)
                {
                    if (testCase is XunitTestCase xunitTestCase)
                    {
                        MergeTraits(traits, xunitTestCase.Traits.ToDictionary(
                            kvp => kvp.Key,
                            kvp => (IReadOnlyCollection<string>)kvp.Value.ToArray(),
                            StringComparer.OrdinalIgnoreCase));
                        continue;
                    }

                    if (testCase is ITestCaseMetadata metadata)
                    {
                        MergeTraits(traits, metadata.Traits);
                    }
                }

                return traits.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (IReadOnlyCollection<string>)kvp.Value.ToArray(),
                    StringComparer.OrdinalIgnoreCase);
            }

            private static void MergeTraits(Dictionary<string, HashSet<string>> target, IReadOnlyDictionary<string, IReadOnlyCollection<string>> source)
            {
                foreach (var kvp in source)
                {
                    if (!target.TryGetValue(kvp.Key, out var values))
                    {
                        values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        target[kvp.Key] = values;
                    }

                    values.UnionWith(kvp.Value);
                }
            }
        }
    }
}
