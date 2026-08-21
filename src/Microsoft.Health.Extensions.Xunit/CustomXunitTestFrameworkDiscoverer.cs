// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using EnsureThat;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// An implementation of <see cref="XunitTestFrameworkDiscoverer"/> that supports discovering tests with parameterized fixtures.
    /// </summary>
    internal sealed class CustomXunitTestFrameworkDiscoverer : XunitTestFrameworkDiscoverer
    {
        private static readonly FieldInfo TestCaseDisplayNameField = typeof(XunitTestCase).GetField("testCaseDisplayName", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly ConcurrentDictionary<string, FixtureArgumentSetTestClass> _variantClassCache = new(StringComparer.Ordinal);

        public CustomXunitTestFrameworkDiscoverer(Assembly assembly, IXunitTestCollectionFactory collectionFactory = null)
            : base(new XunitTestAssembly(assembly, configFileName: null, assembly.GetName().Version, UniqueIDGenerator.ForAssembly(assembly.Location, null)), collectionFactory)
        {
        }

        protected override async ValueTask<bool> FindTestsForType(IXunitTestClass testClass, ITestFrameworkDiscoveryOptions discoveryOptions, Func<ITestCase, ValueTask<bool>> callback)
        {
            EnsureArg.IsNotNull(testClass, nameof(testClass));
            EnsureArg.IsNotNull(callback, nameof(callback));
            EnsureArg.IsNotNull(discoveryOptions, nameof(discoveryOptions));

            try
            {
                return await FindTestsForTypeCore(testClass, discoveryOptions, callback);
            }
            catch (OperationCanceledException)
            {
                // A cancelled run is not a discovery fault. Reporting it as a failing test case would
                // turn pressing Ctrl+C, or a runner-imposed timeout, into a test failure.
                throw;
            }
            catch (Exception ex)
            {
                // An exception thrown out of discovery is reported by xunit.v3 only as an internal
                // diagnostic message, which is suppressed unless the run was started with
                // --xunit-internal-diagnostics on. The class is dropped and the run still reports
                // success, so a broken expansion would look exactly like a class that has no tests.
                // Reporting a test case that fails on execution instead puts the fault in the results
                // and in the exit code, where it cannot be missed.
                Console.WriteLine(
                    $"[FixtureArgumentSets] ERROR: discovery of '{testClass.TestClassName}' failed, so its tests were replaced by a single failing test case. {ex}");

                // The console line above is the authoritative record of the fault. The test case below
                // puts the failure in the results and in the exit code, but it cannot be relied on to
                // carry the cause: a case travels through the ordinary class runner, which builds the
                // class's fixtures first, and a class using fixture argument sets has by definition a
                // fixture taking an argument the failed expansion never produced. That fixture failure
                // is aggregated ahead of this message and is what the report ends up showing. Both
                // ways out of that were tried and are worse - anchoring the case to a class of its own
                // takes it out of the namespace and class filters that would have selected the tests
                // it replaces, and discarding the aggregated failure discards this message with it,
                // leaving the fault unreported again.
                MethodInfo firstMethod = testClass.Methods.FirstOrDefault();
                if (firstMethod == null)
                {
                    // Nothing to hang a test case off. Rethrowing keeps the original xunit behaviour,
                    // which is all that is left, and the console line above is the only warning.
                    throw;
                }

                // The case stays on the class that failed, rather than on a class of its own, so that
                // it is still selected by whatever namespace or class filter would have selected the
                // tests it replaces. FixtureArgumentSetClassRunner keeps the class's fixtures from
                // pre-empting the message it carries.
                // The case stays on the class that failed, rather than on a class of its own, so that
                // it is still selected by whatever namespace or class filter would have selected the
                // tests it replaces. It is given the argument set traits for the same reason, since a
                // leg selecting by those would otherwise pass with the class silently missing.
                var errorTestMethod = new XunitTestMethod(
                    testClass,
                    firstMethod,
                    Array.Empty<object>(),
                    uniqueID: UniqueIDGenerator.ForTestMethod(testClass.UniqueID, firstMethod.Name));

                // The test case is handed to xunit through the callback, which owns it from then on
                // and disposes it with the rest of the discovered cases.
#pragma warning disable CA2000
                var errorTestCase = new DiscoveryErrorTestCase(
                    errorTestMethod,
                    $"{testClass.TestClassName} (fixture argument set discovery)",
                    $"{testClass.UniqueID}-fixture-argument-set-discovery-error",
                    errorMessage: $"Discovering the fixture argument set variants of '{testClass.TestClassName}' failed, so none of its tests ran. {ex}",
                    traits: BuildFaultTraits(testClass));
#pragma warning restore CA2000

                return await callback(errorTestCase);
            }
        }

        /// <summary>
        /// Collects every argument set value the class asked for, so that a failure standing in for
        /// its tests is selected by the same trait filters they would have been.
        /// </summary>
        /// <remarks>
        /// This runs while handling a discovery failure, and reads the same attribute whose contents
        /// may well be what caused it, so it treats any further exception as simply having no traits
        /// to offer. Reporting the failure without them is worth more than not reporting it at all.
        /// </remarks>
        /// <param name="testClass">The class whose expansion failed.</param>
        /// <returns>The traits, keyed by argument enum type name; empty when none could be read.</returns>
        private static Dictionary<string, IReadOnlyCollection<string>> BuildFaultTraits(IXunitTestClass testClass)
        {
            var traits = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var attributes = new List<FixtureArgumentSetsAttribute>();

                if (testClass.Class.GetCustomAttributes(typeof(FixtureArgumentSetsAttribute), inherit: false).SingleOrDefault() is FixtureArgumentSetsAttribute classAttribute)
                {
                    attributes.Add(classAttribute);
                }

                foreach (MethodInfo method in testClass.Methods)
                {
                    if (method.GetCustomAttributes(typeof(FixtureArgumentSetsAttribute), inherit: false).SingleOrDefault() is FixtureArgumentSetsAttribute methodAttribute)
                    {
                        attributes.Add(methodAttribute);
                    }
                }

                foreach (SingleFlag flag in attributes.SelectMany(ExpandEnumFlagsFromAttributeData).SelectMany(dimension => dimension ?? Array.Empty<SingleFlag>()))
                {
                    if (flag.EnumValue == null)
                    {
                        continue;
                    }

                    string key = flag.EnumValue.GetType().Name;
                    if (!traits.TryGetValue(key, out IReadOnlyCollection<string> values))
                    {
                        values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        traits[key] = values;
                    }

                    ((HashSet<string>)values).Add(flag.EnumValue.ToString());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[FixtureArgumentSets] WARNING: the argument set traits of '{testClass.TestClassName}' could not be read, so a trait filter may not select the failure standing in for its tests. {ex}");
            }

            return traits;
        }

        private async ValueTask<bool> FindTestsForTypeCore(IXunitTestClass testClass, ITestFrameworkDiscoveryOptions discoveryOptions, Func<ITestCase, ValueTask<bool>> callback)
        {
            var attribute = testClass.Class.GetCustomAttributes(typeof(FixtureArgumentSetsAttribute), inherit: false).SingleOrDefault() as FixtureArgumentSetsAttribute;
            var methodAttributes = testClass.Methods.ToDictionary(
                method => method,
                method => method.GetCustomAttributes(typeof(FixtureArgumentSetsAttribute), inherit: false).SingleOrDefault() as FixtureArgumentSetsAttribute);

            if (attribute == null && methodAttributes.Values.All(value => value == null))
            {
                return await base.FindTestsForType(testClass, discoveryOptions, callback);
            }

            SingleFlag[][] classLevelOpenParameterSets = Array.Empty<SingleFlag[]>();
            SingleFlag[][] classLevelClosedParameterSets = Array.Empty<SingleFlag[]>();

            if (attribute != null)
            {
                // get the class-level parameter sets in the form (Arg1.OptionA, Arg1.OptionB), (Arg2.OptionA, Arg2.OptionB)
                classLevelOpenParameterSets = ExpandEnumFlagsFromAttributeData(attribute);

                // convert these to the form (Arg1.OptionA, Arg2.OptionA), (Arg1.OptionA, Arg2.OptionB), (Arg1.OptionB, Arg2.OptionA), (Arg1.OptionB, Arg2.OptionB)
                classLevelClosedParameterSets = CartesianProduct(classLevelOpenParameterSets).Select(e => e.ToArray()).ToArray();
            }

            foreach (var method in testClass.Methods)
            {
                var fixtureParameterAttribute = methodAttributes[method];

                if (attribute == null && fixtureParameterAttribute == null)
                {
                    var passthroughTestMethod = new XunitTestMethod(testClass, method, Array.Empty<object>(), uniqueID: UniqueIDGenerator.ForTestMethod(testClass.UniqueID, method.Name));
                    if (!await FindTestsForMethod(passthroughTestMethod, discoveryOptions, callback))
                    {
                        return false;
                    }

                    continue;
                }

                SingleFlag[][] closedSets = classLevelClosedParameterSets;

                if (attribute == null)
                {
                    // Method-level parameter sets with no class-level fallback.
                    SingleFlag[][] methodLevelOpenParameterSets = ExpandEnumFlagsFromAttributeData(fixtureParameterAttribute);
                    closedSets = CartesianProduct(methodLevelOpenParameterSets).Select(e => e.ToArray()).ToArray();
                }
                else if (fixtureParameterAttribute != null)
                {
                    // get the method-level parameter sets in the form (Arg1.OptionA, Arg1.OptionB), (Arg2.OptionA, Arg2.OptionB)
                    SingleFlag[][] methodLevelOpenParameterSets = ExpandEnumFlagsFromAttributeData(fixtureParameterAttribute);

                    bool hasOverride = false;
                    for (int i = 0; i < methodLevelOpenParameterSets.Length; i++)
                    {
                        if (methodLevelOpenParameterSets[i]?.Length > 0)
                        {
                            hasOverride = true;
                        }
                        else
                        {
                            // means take the class-level set
                            methodLevelOpenParameterSets[i] = classLevelOpenParameterSets[i];
                        }
                    }

                    if (hasOverride)
                    {
                        // convert to the form (Arg1.OptionA, Arg2.OptionA), (Arg1.OptionA, Arg2.OptionB), (Arg1.OptionB, Arg2.OptionA), (Arg1.OptionB, Arg2.OptionB)
                        closedSets = CartesianProduct(methodLevelOpenParameterSets).Select(e => e.ToArray()).ToArray();
                    }
                }

                if (closedSets.Length == 0)
                {
                    // A dimension that contributes no flags -- an argument set of zero, a value that
                    // names no single flag, or a null entry -- collapses the cartesian product to
                    // nothing, and this method then produces no test cases at all. The run stays
                    // green with the tests simply absent, so say so rather than let them vanish.
                    Console.WriteLine(
                        $"[FixtureArgumentSets] WARNING: '{testClass.TestClassName}.{method.Name}' expanded to no fixture argument sets, so none of its tests will run. " +
                        "Check that every argument set names at least one single-valued flag of a [Flags] enum.");
                }

                foreach (SingleFlag[] closedVariant in closedSets)
                {
                    // Every variant stays in the class's original collection. xUnit's unit of
                    // parallelization is the collection, so giving each variant its own would let
                    // classes an explicit [Collection] deliberately grouped together -- and the
                    // variants of a single class -- start running concurrently. Only the identifiers
                    // below carry the variant, so the collection keeps its original grouping.
                    var variantKey = BuildVariantKey(testClass.TestCollection, closedVariant);
                    var classKey = BuildVariantClassKey(variantKey, testClass.Class);
                    var closedVariantTestClass = _variantClassCache.GetOrAdd(
                        classKey,
                        _ => new FixtureArgumentSetTestClass(
                            testClass.Class,
                            testClass.TestCollection,
                            closedVariant,
                            UniqueIDGenerator.ForTestClass(testClass.TestCollection.UniqueID, classKey)));

                    var closedVariantTestMethod = new FixtureArgumentSetTestMethod(closedVariantTestClass, method, closedVariant, uniqueId: UniqueIDGenerator.ForTestMethod(closedVariantTestClass.UniqueID, method.Name));

                    closedVariantTestMethod.UpdateArgumentsFromMethod();

                    // xUnit builds the display name from the class and method names only, so every
                    // variant of a method would otherwise be reported under an identical name and
                    // a failure could not be attributed to a specific fixture argument set.
                    var variantArguments = closedVariant;
                    Func<ITestCase, ValueTask<bool>> variantCallback = testCase =>
                    {
                        ApplyVariantDisplayName(testCase, variantArguments);
                        return callback(testCase);
                    };

                    if (!await FindTestsForMethod(closedVariantTestMethod, discoveryOptions, variantCallback))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Appends the fixture argument set to a discovered test case's display name, so that the
        /// variants of a test can be told apart in test results.
        /// </summary>
        /// <param name="testCase">The discovered test case.</param>
        /// <param name="closedVariant">The fixture arguments the test case was discovered for.</param>
        private static void ApplyVariantDisplayName(ITestCase testCase, SingleFlag[] closedVariant)
        {
            if (closedVariant.Length == 0)
            {
                return;
            }

            if (testCase is not XunitTestCase xunitTestCase)
            {
                // Without the suffix every variant of this method reports under the same name, so
                // a failure cannot be attributed to a fixture argument set. Every test case type
                // xunit.v3 produces derives from XunitTestCase, so this only happens if a custom
                // discoverer introduces its own type -- say so rather than silently mis-naming.
                Console.WriteLine(
                    $"[FixtureArgumentSets] WARNING: Test case '{testCase.TestCaseDisplayName}' is {testCase.GetType().Name}, not {nameof(XunitTestCase)}. " +
                    "Its fixture argument set will NOT be appended to the display name, so variants of this test will be indistinguishable in test results.");
                return;
            }

            if (TestCaseDisplayNameField == null)
            {
                throw new InvalidOperationException(
                    "Unable to name fixture argument set variants because XunitTestCase.testCaseDisplayName was not found. " +
                    "This usually means the xunit.v3 version changed; see Microsoft.Health.Extensions.Xunit.");
            }

            var suffix = $" ({string.Join(", ", closedVariant.Select(argument => argument.EnumValue))})";
            var displayName = xunitTestCase.TestCaseDisplayName;

            if (displayName.EndsWith(suffix, StringComparison.Ordinal))
            {
                return;
            }

            TestCaseDisplayNameField.SetValue(xunitTestCase, displayName + suffix);
        }

        /// <summary>
        /// Builds a key that distinguishes one fixture argument set from another within a collection.
        /// </summary>
        /// <param name="sourceCollection">The collection the test class belongs to.</param>
        /// <param name="closedVariant">The fixture arguments the variant was closed over.</param>
        /// <returns>A stable key for the variant.</returns>
        private static string BuildVariantKey(IXunitTestCollection sourceCollection, IReadOnlyList<SingleFlag> closedVariant)
        {
            var variantKey = string.Join(
                ",",
                closedVariant.Select(argument => $"{argument.EnumValue.GetType().AssemblyQualifiedName}={Convert.ToInt64(argument.EnumValue)}"));

            return $"{sourceCollection.UniqueID}|{variantKey}";
        }

        /// <summary>
        /// Builds a key that identifies one variant of one test class.
        /// </summary>
        /// <param name="variantKey">The key returned by <see cref="BuildVariantKey"/>.</param>
        /// <param name="testClass">The test class the variant was derived from.</param>
        /// <returns>A stable key for the variant class.</returns>
        /// <remarks>
        /// The variants of a class share a collection and a class name, so the class name alone no
        /// longer separates them and this key is what the variant class cache is keyed on. It is
        /// also passed as the seed for the class's unique ID, but only reaches it for a variant that
        /// carries no arguments: <see cref="FixtureArgumentSetTestClass"/> recomputes the unique ID
        /// from its own argument values whenever it has any, which is the usual case.
        /// </remarks>
        private static string BuildVariantClassKey(string variantKey, Type testClass)
        {
            return $"{variantKey}|{testClass.AssemblyQualifiedName}";
        }

        private static SingleFlag[][] ExpandEnumFlagsFromAttributeData(FixtureArgumentSetsAttribute attribute)
        {
            bool IsPowerOfTwo(long x)
            {
                return (x != 0) && ((x & (x - 1)) == 0);
            }

            IEnumerable<SingleFlag> GetSingleValuedFlags(Enum e)
            {
                if (e is null)
                {
                    yield break;
                }

                var enumAsLong = Convert.ToInt64(e);

                foreach (Enum value in Enum.GetValues(e.GetType()))
                {
                    var flagAsLong = Convert.ToInt64(value);
                    if (IsPowerOfTwo(flagAsLong))
                    {
                        if ((enumAsLong & flagAsLong) != 0)
                        {
                            yield return new SingleFlag(value);
                        }
                    }
                }
            }

            return attribute.GetArgumentSets()
                .Select(e => GetSingleValuedFlags(e).ToArray())
                .ToArray();
        }

        /// <summary>
        /// Computes the cartesian product of a sequence of sequences.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements of the input sequences.</typeparam>
        /// <param name="sequences">The input sequence.</param>
        /// <returns>An <see cref="IEnumerable{T}"/> that contains the cartesian product of the input sequences.</returns>
        public static IEnumerable<IEnumerable<TSource>> CartesianProduct<TSource>(IEnumerable<IEnumerable<TSource>> sequences)
        {
            EnsureArg.IsNotNull(sequences, nameof(sequences));

            IEnumerable<IEnumerable<TSource>> emptyProduct = new[] { Enumerable.Empty<TSource>() };

            return sequences.Aggregate(
                emptyProduct,
                (accumulator, sequence) => accumulator.SelectMany(a => sequence.Select(s => a.Concat(Enumerable.Repeat(s, 1)))));
        }
    }
}
