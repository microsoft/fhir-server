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

            var tracker = new CallbackTracker(callback);

            try
            {
                return await FindTestsForTypeCore(testClass, discoveryOptions, tracker);
            }
            catch (OperationCanceledException)
            {
                // A cancelled run is not a discovery fault. Reporting it as a failing test case would
                // turn pressing Ctrl+C, or a runner-imposed timeout, into a test failure.
                throw;
            }
            catch (Exception ex) when (tracker.IsCallbackFailure(ex))
            {
                // The caller's own sink threw. Describing that as a failure to expand this class would
                // be wrong, and handing the caller a further test case is unlikely to fare any better,
                // so it goes back to xunit to deal with as it would for any other class.
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
                //
                // Reaching here means the failure was outside the per-method loop, which reports its
                // own faults, so it belongs to the class as a whole and no method of it was expanded.
                //
                // testClass.Methods includes every public method, property accessors among them, so
                // the anchor is chosen from those xunit would treat as tests: a fault reported
                // against a property getter names nothing a reader can act on, and the getter carries
                // none of the traits a CI leg selects the class's tests by.
                MethodInfo firstMethod = testClass.Methods.FirstOrDefault(IsTestMethod) ?? testClass.Methods.FirstOrDefault();
                if (firstMethod == null)
                {
                    // Nothing to hang a test case off. Rethrowing keeps the original xunit behaviour,
                    // which is all that is left.
                    Console.WriteLine(
                        $"[FixtureArgumentSets] ERROR: discovery of '{testClass.TestClassName}' failed and it declares no method to report the failure against. {ex}");
                    throw;
                }

                return await ReportDiscoveryFault(
                    testClass,
                    firstMethod,
                    tracker,
                    ex,
                    $"Discovering the fixture argument set variants of '{testClass.TestClassName}' failed, so none of its tests ran.");
            }
        }

        /// <summary>
        /// Determines whether xunit would treat a method as a test.
        /// </summary>
        /// <param name="method">The method to inspect.</param>
        /// <returns><c>true</c> when the method carries a fact or theory attribute.</returns>
        private static bool IsTestMethod(MethodInfo method) =>
            method.GetCustomAttributes(typeof(FactAttribute), inherit: true).Length > 0;

        /// <summary>
        /// Reports a discovery failure as a test case that fails when the run reaches it.
        /// </summary>
        /// <param name="testClass">The class being discovered.</param>
        /// <param name="method">The method to report the failure against.</param>
        /// <param name="tracker">The callback discovered test cases are handed to.</param>
        /// <param name="exception">The failure being reported.</param>
        /// <param name="summary">A sentence saying what was lost, used to open the failure message.</param>
        /// <returns>Whether discovery should continue, as the callback reported it.</returns>
        /// <remarks>
        /// The console line written here is the authoritative record of the fault. The test case puts
        /// the failure in the results and in the exit code, but it cannot be relied on to carry the
        /// cause: a case travels through the ordinary class runner, which builds the class's fixtures
        /// first, and a class using fixture argument sets typically has a fixture taking an argument
        /// the failed expansion never produced. That fixture failure is aggregated ahead of this
        /// message and is what the report then shows. Both ways out of that were tried and are worse -
        /// anchoring the case to a class of its own takes it out of the namespace and class filters
        /// that would have selected the tests it replaces, and discarding the aggregated failure
        /// discards this message with it, leaving the fault unreported again.
        /// </remarks>
        private static async ValueTask<bool> ReportDiscoveryFault(
            IXunitTestClass testClass,
            MethodInfo method,
            CallbackTracker tracker,
            Exception exception,
            string summary)
        {
            Console.WriteLine($"[FixtureArgumentSets] ERROR: {summary} It was replaced by a failing test case. {exception}");

            // The case stays on the class that failed, rather than on a class of its own, so that it
            // is still selected by whatever namespace or class filter would have selected the tests it
            // replaces, and it carries their traits so that a leg selecting by those cannot pass with
            // the tests silently missing.
            var errorTestMethod = new XunitTestMethod(
                testClass,
                method,
                Array.Empty<object>(),
                uniqueID: UniqueIDGenerator.ForTestMethod(testClass.UniqueID, method.Name));

            // The test case is handed to xunit through the callback, which owns it from then on
            // and disposes it with the rest of the discovered cases.
#pragma warning disable CA2000
            var errorTestCase = new DiscoveryErrorTestCase(
                errorTestMethod,
                $"{testClass.TestClassName}.{method.Name} (fixture argument set discovery)",
                $"{testClass.UniqueID}-{method.Name}-fixture-argument-set-discovery-error",
                errorMessage: $"{summary} {exception}",
                traits: BuildFaultTraits(testClass, errorTestMethod));
#pragma warning restore CA2000

            return await tracker.Invoke(errorTestCase);
        }

        /// <summary>
        /// Collects the traits a failure standing in for a method's tests must carry to be selected by
        /// the same filters they would have been.
        /// </summary>
        /// <remarks>
        /// Two kinds of trait matter. The ordinary ones the method and its class declare come from the
        /// anchoring method itself, and CI legs combine them with argument set values in filters such
        /// as <c>(DataStore=CosmosDb)&amp;(Category=ExportLongRunning)</c>, so leaving either out lets
        /// the leg that would have run these tests pass without them. The argument set values are the
        /// ones the expansion never got far enough to produce, so they have to be read back from the
        /// attributes here.
        /// <para>
        /// This runs while handling a discovery failure, and reads the same attribute whose contents
        /// may well be what caused it, so it treats any further exception as simply having no argument
        /// set traits to offer. Reporting the failure with fewer traits is worth more than not
        /// reporting it at all.
        /// </para>
        /// </remarks>
        /// <param name="testClass">The class whose expansion failed.</param>
        /// <param name="anchor">The method the failure is reported against.</param>
        /// <returns>The traits, keyed by trait name; empty when none could be read.</returns>
        private static Dictionary<string, IReadOnlyCollection<string>> BuildFaultTraits(IXunitTestClass testClass, XunitTestMethod anchor)
        {
            var traits = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);

            void Add(string key, string value)
            {
                if (!traits.TryGetValue(key, out IReadOnlyCollection<string> values))
                {
                    values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    traits[key] = values;
                }

                ((HashSet<string>)values).Add(value);
            }

            foreach (KeyValuePair<string, IReadOnlyCollection<string>> trait in anchor.Traits)
            {
                foreach (string value in trait.Value)
                {
                    Add(trait.Key, value);
                }
            }

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

                    Add(flag.EnumValue.GetType().Name, flag.EnumValue.ToString());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[FixtureArgumentSets] WARNING: the argument set traits of '{testClass.TestClassName}' could not be read, so a trait filter may not select the failure standing in for its tests. {ex}");
            }

            return traits;
        }

        private async ValueTask<bool> FindTestsForTypeCore(IXunitTestClass testClass, ITestFrameworkDiscoveryOptions discoveryOptions, CallbackTracker tracker)
        {
            var attribute = testClass.Class.GetCustomAttributes(typeof(FixtureArgumentSetsAttribute), inherit: false).SingleOrDefault() as FixtureArgumentSetsAttribute;
            var methodAttributes = testClass.Methods.ToDictionary(
                method => method,
                method => method.GetCustomAttributes(typeof(FixtureArgumentSetsAttribute), inherit: false).SingleOrDefault() as FixtureArgumentSetsAttribute);

            if (attribute == null && methodAttributes.Values.All(value => value == null))
            {
                return await base.FindTestsForType(testClass, discoveryOptions, tracker.Invoke);
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
                // Each method is expanded on its own so that one method's misdeclared argument set
                // costs only that method. Letting the failure out of the loop would leave the methods
                // already published running while the methods after it disappeared, under a single
                // failure claiming the whole class had not run.
                bool continueDiscovery;
                try
                {
                    continueDiscovery = await FindTestsForMethodCore(testClass, method, attribute, methodAttributes[method], classLevelOpenParameterSets, classLevelClosedParameterSets, discoveryOptions, tracker);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (!tracker.IsCallbackFailure(ex))
                {
                    continueDiscovery = await ReportDiscoveryFault(
                        testClass,
                        method,
                        tracker,
                        ex,
                        $"Discovering the fixture argument set variants of '{testClass.TestClassName}.{method.Name}' failed, so none of that method's tests ran. Other methods of the class were discovered normally.");
                }

                if (!continueDiscovery)
                {
                    return false;
                }
            }

            return true;
        }

        private async ValueTask<bool> FindTestsForMethodCore(
            IXunitTestClass testClass,
            MethodInfo method,
            FixtureArgumentSetsAttribute attribute,
            FixtureArgumentSetsAttribute fixtureParameterAttribute,
            SingleFlag[][] classLevelOpenParameterSets,
            SingleFlag[][] classLevelClosedParameterSets,
            ITestFrameworkDiscoveryOptions discoveryOptions,
            CallbackTracker tracker)
        {
            if (attribute == null && fixtureParameterAttribute == null)
            {
                var passthroughTestMethod = new XunitTestMethod(testClass, method, Array.Empty<object>(), uniqueID: UniqueIDGenerator.ForTestMethod(testClass.UniqueID, method.Name));
                return await FindTestsForMethod(passthroughTestMethod, discoveryOptions, tracker.Invoke);
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
                // Reaching here means an argument set was declared, so a product of nothing is a
                // misconfiguration -- an argument set of zero, a value naming no single flag, or a
                // null entry -- and every test on this method would otherwise be absent from a run
                // that still reported success. Throwing hands it to the handler in FindTestsForType,
                // which reports it as a failing test case so it shows up in the results and in the
                // exit code. Discovery over every test assembly in this repository produces no such
                // case, so this only fires on a class that is genuinely misdeclared.
                throw new InvalidOperationException(
                    $"'{testClass.TestClassName}.{method.Name}' expanded to no fixture argument sets, so none of its tests would run. " +
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
                    return tracker.Invoke(testCase);
                };

                if (!await FindTestsForMethod(closedVariantTestMethod, discoveryOptions, variantCallback))
                {
                    return false;
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

        /// <summary>
        /// Wraps the callback discovered test cases are handed to, so that a failure inside it can be
        /// told apart from a failure to expand fixture argument sets.
        /// </summary>
        /// <remarks>
        /// The callback belongs to xunit rather than to this discoverer. Reporting a failure inside it
        /// as a fixture argument set fault would name the wrong culprit, and would try to report that
        /// through the very callback that has just thrown.
        /// </remarks>
        private sealed class CallbackTracker
        {
            private readonly Func<ITestCase, ValueTask<bool>> _callback;
            private Exception _failure;

            public CallbackTracker(Func<ITestCase, ValueTask<bool>> callback)
            {
                _callback = callback;
            }

            public async ValueTask<bool> Invoke(ITestCase testCase)
            {
                try
                {
                    return await _callback(testCase);
                }
                catch (Exception ex)
                {
                    _failure = ex;
                    throw;
                }
            }

            public bool IsCallbackFailure(Exception exception) => ReferenceEquals(exception, _failure);
        }
    }
}
