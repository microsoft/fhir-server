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
            : base(new XunitTestAssembly(assembly, configFileName: null, assembly.GetName().Version, UniqueIDGenerator.ForAssembly(assembly.Location, null)))
        {
        }

        public override async ValueTask RunTestCases(IReadOnlyCollection<IXunitTestCase> testCases, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions, CancellationToken cancellationToken)
        {
            var runner = new FixtureArgumentSetAssemblyRunner();
            await runner.Run(TestAssembly, testCases, executionMessageSink, executionOptions, cancellationToken);
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

        /// <summary>
        /// Runs a test class, injecting the fixture argument set values its variant was expanded with.
        /// </summary>
        /// <remarks>
        /// Under xunit.v2 this runner also ran test methods in the execution context captured right
        /// after each class fixture was constructed, so an <see cref="AsyncLocal{T}"/> written by a
        /// fixture constructor could be read by the tests. That is not reproducible on xunit.v3:
        /// v2 built fixtures in a synchronous <c>CreateClassFixture</c> override, while v3 builds
        /// them inside <c>FixtureMappingManager.GetFixture</c>, an async method whose state machine
        /// restores the caller's execution context as it returns. Any such write is therefore
        /// already discarded before this runner regains control, and no capture point outside that
        /// method can see it. Fixtures that need to share ambient state with their tests have to
        /// expose it as a member instead of relying on the execution context.
        /// </remarks>
        private sealed class FixtureArgumentSetClassRunner : XunitTestClassRunner
        {
            private static readonly FieldInfo FixtureCacheField = typeof(FixtureMappingManager)
                .GetField("fixtureCache", BindingFlags.Instance | BindingFlags.NonPublic);

            private static readonly FieldInfo ParentMappingManagerField = typeof(FixtureMappingManager)
                .GetField("parentMappingManager", BindingFlags.Instance | BindingFlags.NonPublic);

            protected override async ValueTask<bool> OnTestClassStarting(XunitTestClassRunnerContext context)
            {
                InjectFixtureArguments(context);
                return await base.OnTestClassStarting(context);
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

            private static void InjectFixtureArguments(XunitTestClassRunnerContext context)
            {
                if (context?.TestClass == null)
                {
                    return;
                }

                if (FixtureCacheField == null)
                {
                    throw new InvalidOperationException("Unable to inject fixture arguments because FixtureMappingManager.fixtureCache was not found.");
                }

                if (ParentMappingManagerField == null)
                {
                    throw new InvalidOperationException("Unable to inject fixture arguments because FixtureMappingManager.parentMappingManager was not found.");
                }

                var cacheOwner = context.ClassFixtureMappings;
                if (ParentMappingManagerField.GetValue(cacheOwner) is FixtureMappingManager parentMappingManager)
                {
                    cacheOwner = parentMappingManager;
                }

                var cache = FixtureCacheField.GetValue(cacheOwner) as IDictionary<Type, object>;
                if (cache == null)
                {
                    throw new TestPipelineException("Unable to inject fixture arguments because the xUnit fixture cache could not be read.");
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

                // The cache being written to is scoped to the collection, and the variants of a class
                // share a collection so that an explicit [Collection] keeps its classes serialized.
                // Every variant therefore writes over the previous one's arguments: adding only what
                // is missing would leave the first variant's values in place and hand every later
                // variant the wrong fixture. Types this class expects but could not resolve are
                // evicted for the same reason -- a stale value is worse than no value, because it
                // silently constructs a fixture for the wrong argument set.
                foreach (Type parameterType in fixtureParameterTypes)
                {
                    if (resolvedArguments.TryGetValue(parameterType, out var resolvedValue))
                    {
                        cache[parameterType] = resolvedValue;
                    }
                    else
                    {
                        cache.Remove(parameterType);
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
    }
}
