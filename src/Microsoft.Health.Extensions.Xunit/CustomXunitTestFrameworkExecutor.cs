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
            await runner.Run(TestAssembly, testCases, executionMessageSink, executionOptions, cancellationToken);
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

                        if (AssemblyFixtureTypesField == null)
                        {
                            throw new InvalidOperationException("Unable to initialize assembly fixtures because XunitTestAssembly.assemblyFixtureTypes was not found.");
                        }

                        AssemblyFixtureTypesField.SetValue(this, new Lazy<IReadOnlyCollection<Type>>(() => _assemblyFixtureTypes));
                    }

                    return _assemblyFixtureTypes;
                }
            }
        }

        private sealed class FixtureArgumentSetAssemblyRunner : XunitTestAssemblyRunner
        {
            protected override async ValueTask<RunSummary> RunTestCollection(XunitTestAssemblyRunnerContext context, IXunitTestCollection testCollection, IReadOnlyCollection<IXunitTestCase> testCases)
            {
                var testCaseOrderer = context.AssemblyTestCaseOrderer ?? DefaultTestCaseOrderer.Instance ?? StableFallbackTestCaseOrderer.Instance;
                var runner = new FixtureArgumentSetCollectionRunner();
                var summary = await runner.Run(testCollection, testCases, context.ExplicitOption, context.MessageBus, testCaseOrderer, context.Aggregator, context.CancellationTokenSource, context.AssemblyFixtureMappings);
                return summary;
            }
        }

        private sealed class FixtureArgumentSetCollectionRunner : XunitTestCollectionRunner
        {
            protected override async ValueTask<RunSummary> RunTestClass(XunitTestCollectionRunnerContext context, IXunitTestClass testClass, IReadOnlyCollection<IXunitTestCase> testCases)
            {
                var testCaseOrderer = context.TestCaseOrderer ?? DefaultTestCaseOrderer.Instance ?? StableFallbackTestCaseOrderer.Instance;
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

            private ExecutionContext _executionContext;

            protected override async ValueTask<bool> OnTestClassStarting(XunitTestClassRunnerContext context)
            {
                InjectFixtureArguments(context);
                var result = await base.OnTestClassStarting(context);
                _executionContext = ExecutionContext.Capture();
                return result;
            }

            protected override async ValueTask<bool> OnTestClassFinished(XunitTestClassRunnerContext context, RunSummary summary)
            {
                _executionContext?.Dispose();
                _executionContext = null;
                return await base.OnTestClassFinished(context, summary);
            }

            protected override ValueTask<RunSummary> RunTestMethod(XunitTestClassRunnerContext context, IXunitTestMethod testMethod, IReadOnlyCollection<IXunitTestCase> testCases, object[] constructorArguments)
            {
                if (_executionContext == null)
                {
                    return base.RunTestMethod(context, testMethod, testCases, constructorArguments);
                }

                ValueTask<RunSummary> summary = default;
                ExecutionContext.Run(_executionContext, _ => summary = base.RunTestMethod(context, testMethod, testCases, constructorArguments), null);
                return summary;
            }

            protected override ValueTask<object> GetConstructorArgument(XunitTestClassRunnerContext context, ConstructorInfo constructor, int index, ParameterInfo parameter)
            {
                if (context?.TestClass is FixtureArgumentSetTestClass fixtureTestClass)
                {
                    var fixtureArguments = fixtureTestClass.GetFixtureArguments();
                    if (fixtureArguments.Count > 0)
                    {
                        var enumValue = fixtureArguments
                            .Select(argument => argument.EnumValue)
                            .FirstOrDefault(value => value != null && parameter.ParameterType == value.GetType());

                        if (enumValue != null)
                        {
                            return new ValueTask<object>(enumValue);
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

                        foreach (var parameterType in constructor.GetParameters()
                            .Select(parameter => parameter.ParameterType)
                            .Where(parameterType => parameterType.IsEnum))
                        {
                            fixtureParameterTypes.Add(parameterType);
                        }
                    }
                }

                var resolvedArguments = new Dictionary<Type, object>();

                foreach (var argument in fixtureArguments
                    .Select(enumValue => (EnumValue: enumValue, ArgumentType: enumValue.GetType())))
                {
                    foreach (var parameterType in fixtureParameterTypes
                        .Where(parameterType => string.Equals(parameterType.FullName, argument.ArgumentType.FullName, StringComparison.Ordinal)
                            && !resolvedArguments.ContainsKey(parameterType)))
                    {
                        resolvedArguments[parameterType] = parameterType == argument.ArgumentType
                            ? argument.EnumValue
                            : Enum.ToObject(parameterType, Convert.ToInt64(argument.EnumValue));
                    }
                }

                if (resolvedArguments.Count == 0 && fixtureParameterTypes.Count > 0)
                {
                    var traits = GetTraits(context.TestCases);
                    foreach (var parameterType in fixtureParameterTypes
                        .Where(parameterType => traits.TryGetValue(parameterType.Name, out var values) && values.Count > 0))
                    {
                        var traitKey = parameterType.Name;
                        var values = traits[traitKey];

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

                foreach (var resolvedArgument in resolvedArguments
                    .Where(resolvedArgument => !cache.ContainsKey(resolvedArgument.Key)))
                {
                    cache[resolvedArgument.Key] = resolvedArgument.Value;
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

                    MergeTraits(traits, ((ITestCaseMetadata)testCase).Traits);
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

        private sealed class StableFallbackTestCaseOrderer : ITestCaseOrderer
        {
            internal static ITestCaseOrderer Instance { get; } = new StableFallbackTestCaseOrderer();

            public IReadOnlyCollection<TTestCase> OrderTestCases<TTestCase>(IReadOnlyCollection<TTestCase> testCases)
                where TTestCase : ITestCase
            {
                ArgumentNullException.ThrowIfNull(testCases);

                return testCases
                    .OrderBy(testCase => GetSortKey(testCase))
                    .ToArray();
            }

            private static string GetSortKey<TTestCase>(TTestCase testCase)
            {
                if (testCase is ITestCaseMetadata metadata)
                {
                    return metadata.TestCaseDisplayName ?? string.Empty;
                }

                return testCase?.ToString() ?? string.Empty;
            }
        }
    }
}
