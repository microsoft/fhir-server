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
            catch (Exception ex) when (ShouldRethrowRatherThanReport(ex, tracker.IsCallbackFailure(ex)))
            {
                // Either the run was cancelled, or the caller's own sink threw. Neither is a failure
                // to expand fixture argument sets, so neither is reported as one.
                throw;
            }
            catch (Exception ex)
            {
                // An exception thrown out of discovery is reported by xunit.v3 only as a diagnostic
                // message, which is suppressed unless the run was started with --xunit-diagnostics.
                // The class is dropped and the run still reports success, so a broken expansion would
                // look exactly like a class that has no tests. Reporting a test case that fails on
                // execution instead puts the fault in the results and in the exit code, where it
                // cannot be missed.
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
        /// Decides whether an exception thrown while expanding fixture argument sets goes back to
        /// xunit unchanged, or is reported as a failing test case standing in for the tests it lost.
        /// </summary>
        /// <remarks>
        /// Reporting a fault as a test is what stops a broken expansion from leaving a run green with
        /// its tests silently absent, but it is only right for a failure that belongs to the expansion.
        /// Two do not. A cancelled run is not a fault at all, and turning it into a test case would
        /// make pressing Ctrl+C, or a runner-imposed timeout, produce a red test that names a class
        /// with nothing wrong with it. A failure raised by xunit's own callback is not ours either,
        /// and the only way to report it would be to hand that same callback another test case.
        /// <para>
        /// This is a separate function so that the decision can be pinned directly. Reached only
        /// through a catch clause, the cancellation case in particular needs a run to be cancelled at
        /// the moment a class is being expanded, which no scenario can arrange reliably.
        /// </para>
        /// </remarks>
        /// <param name="exception">The exception that ended the expansion.</param>
        /// <param name="isCallbackFailure">Whether the exception came from xunit's own callback.</param>
        /// <returns><c>true</c> to rethrow; <c>false</c> to report the failure as a test case.</returns>
        internal static bool ShouldRethrowRatherThanReport(Exception exception, bool isCallbackFailure) =>
            exception is OperationCanceledException || isCallbackFailure;

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
        /// <para>
        /// One case is reported per combination of argument set values rather than one case carrying
        /// them all, because a trait exclusion filter drops a case when <em>any</em> of its values
        /// under the named trait matches. A single case declaring both <c>DataStore=CosmosDb</c> and
        /// <c>DataStore=SqlServer</c> would be dropped by the SQL leg's
        /// <c>--filter-not-trait DataStore=CosmosDb</c> and by the Cosmos leg's
        /// <c>--filter-not-trait DataStore=SqlServer</c> alike, so the fault would be reported to
        /// nobody while both legs stayed green.
        /// </para>
        /// <para>
        /// A case standing for a combination is anchored to that combination's variant class, the same
        /// class the tests it replaces would have run in. That is what lets the failure carry its own
        /// message: the executor takes the fixture's argument from the variant class it is running,
        /// and only falls back to reading it off the case's traits when there is no variant class to
        /// ask - a fallback that cannot tell one combination from another and refuses to guess. With
        /// the argument supplied the fixture builds, nothing is aggregated ahead of the case, and the
        /// report shows the discovery failure rather than a fixture that could not be constructed.
        /// </para>
        /// </remarks>
        private async ValueTask<bool> ReportDiscoveryFault(
            IXunitTestClass testClass,
            MethodInfo method,
            CallbackTracker tracker,
            Exception exception,
            string summary)
        {
            Console.WriteLine($"[FixtureArgumentSets] ERROR: {summary} It was replaced by a failing test case. {exception}");

            foreach (IReadOnlyList<SingleFlag> combination in BuildFaultArgumentSetCombinations(ReadFaultArgumentSetValues(testClass)))
            {
                string displaySuffix = combination.Count == 0
                    ? string.Empty
                    : $": {string.Join(", ", combination.Select(flag => flag.EnumValue.ToString()))}";
                string idSuffix = combination.Count == 0
                    ? string.Empty
                    : $"-{string.Join("-", combination.Select(flag => $"{flag.EnumValue.GetType().Name}.{flag.EnumValue}"))}";

                // The case stays on the class that failed - either the class itself, or the variant of
                // it the lost tests would have run in - rather than on a class of its own, so that it
                // is still selected by whatever namespace or class filter would have selected them,
                // and it carries their traits so that a leg selecting by those cannot pass with the
                // tests silently missing.
                IXunitTestClass anchorClass = testClass;
                if (combination.Count > 0)
                {
                    SingleFlag[] closedVariant = combination.ToArray();
                    string classKey = BuildVariantClassKey(BuildVariantKey(testClass.TestCollection, closedVariant), testClass.Class);
                    anchorClass = _variantClassCache.GetOrAdd(
                        classKey,
                        _ => new FixtureArgumentSetTestClass(
                            testClass.Class,
                            testClass.TestCollection,
                            closedVariant,
                            UniqueIDGenerator.ForTestClass(testClass.TestCollection.UniqueID, classKey)));
                }

                var errorTestMethod = new XunitTestMethod(
                    anchorClass,
                    method,
                    Array.Empty<object>(),
                    uniqueID: UniqueIDGenerator.ForTestMethod(anchorClass.UniqueID, method.Name));

                // The test case is handed to xunit through the callback, which owns it from then on
                // and disposes it with the rest of the discovered cases.
#pragma warning disable CA2000
                var errorTestCase = new DiscoveryErrorTestCase(
                    errorTestMethod,
                    $"{testClass.TestClassName}.{method.Name} (fixture argument set discovery{displaySuffix})",
                    $"{testClass.UniqueID}-{method.Name}-fixture-argument-set-discovery-error{idSuffix}",
                    errorMessage: $"{summary} {exception}",
                    traits: BuildFaultTraits(errorTestMethod, combination));
#pragma warning restore CA2000

                if (!await tracker.Invoke(errorTestCase))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Closes the argument set values a failed class declared over each other, one dimension per
        /// enum type, so that each combination can be reported as its own failure.
        /// </summary>
        /// <param name="argumentSetValues">Every argument set value read off the class, in any order.</param>
        /// <returns>
        /// One list per combination. A class declaring no argument set values yields a single empty
        /// combination, so a fault is always reported at least once.
        /// </returns>
        /// <remarks>
        /// Values are grouped by enum type because that is what a dimension is: an attribute takes one
        /// flags enum per fixture constructor parameter, and the variants a class expands to are the
        /// product of the single flags set within each. Reproducing that product here means a leg
        /// selecting a variant by trait selects the failure standing in for it, and a leg excluding a
        /// variant by trait excludes only that one and still sees the rest.
        /// </remarks>
        internal static IReadOnlyList<IReadOnlyList<SingleFlag>> BuildFaultArgumentSetCombinations(IEnumerable<SingleFlag> argumentSetValues)
        {
            EnsureArg.IsNotNull(argumentSetValues, nameof(argumentSetValues));

            IEnumerable<IEnumerable<SingleFlag>> dimensions = argumentSetValues
                .Where(flag => flag.EnumValue != null)
                .GroupBy(flag => flag.EnumValue.GetType())
                .OrderBy(dimension => dimension.Key.Name, StringComparer.Ordinal)
                .Select(dimension => (IEnumerable<SingleFlag>)dimension
                    .GroupBy(flag => flag.EnumValue)
                    .Select(values => values.First())
                    .ToArray())
                .ToArray();

            return CartesianProduct(dimensions)
                .Select(combination => (IReadOnlyList<SingleFlag>)combination.ToArray())
                .ToArray();
        }

        /// <summary>
        /// Reads every argument set value a class and its methods declare, for use in reporting a
        /// discovery failure.
        /// </summary>
        /// <remarks>
        /// This runs while handling a discovery failure, and reads the same attributes whose contents
        /// may well be what caused it, so each attribute is read and expanded on its own and any
        /// further exception costs only that one attribute's values. Reporting the failure with fewer
        /// traits is worth more than not reporting it at all.
        /// <para>
        /// Every method's values are pooled, not just the anchor method's, because a class-level fault
        /// loses every method. Pooling can only add combinations, and an extra combination reports the
        /// fault once more rather than hiding it, whereas a missing one is a leg that passes with the
        /// tests absent.
        /// </para>
        /// </remarks>
        /// <param name="testClass">The class whose expansion failed.</param>
        /// <returns>The declared values; empty when none could be read.</returns>
        private static SingleFlag[] ReadFaultArgumentSetValues(IXunitTestClass testClass)
        {
            var attributes = new List<FixtureArgumentSetsAttribute>();

            // Each attribute is read on its own so that the one whose contents may have caused the
            // fault in the first place costs only its own values, rather than emptying the pool and
            // leaving the failure with no traits for a leg's filter to select it by.
            TryAdd(() => testClass.Class.GetCustomAttributes(typeof(FixtureArgumentSetsAttribute), inherit: false).SingleOrDefault() as FixtureArgumentSetsAttribute);

            foreach (MethodInfo method in testClass.Methods)
            {
                MethodInfo closedMethod = method;
                TryAdd(() => closedMethod.GetCustomAttributes(typeof(FixtureArgumentSetsAttribute), inherit: false).SingleOrDefault() as FixtureArgumentSetsAttribute);
            }

            return attributes
                .SelectMany(SafeExpand)
                .Where(flag => flag.EnumValue != null)
                .ToArray();

            void TryAdd(Func<FixtureArgumentSetsAttribute> read)
            {
                try
                {
                    if (read() is FixtureArgumentSetsAttribute attribute)
                    {
                        attributes.Add(attribute);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[FixtureArgumentSets] WARNING: some argument set traits of '{testClass.TestClassName}' could not be read, so a trait filter may not select the failure standing in for its tests. {ex}");
                }
            }

            IEnumerable<SingleFlag> SafeExpand(FixtureArgumentSetsAttribute attribute)
            {
                try
                {
                    return ExpandEnumFlagsFromAttributeData(attribute).SelectMany(dimension => dimension ?? Array.Empty<SingleFlag>()).ToArray();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[FixtureArgumentSets] WARNING: some argument set traits of '{testClass.TestClassName}' could not be expanded, so a trait filter may not select the failure standing in for its tests. {ex}");
                    return Array.Empty<SingleFlag>();
                }
            }
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
        /// ones the expansion never got far enough to produce, so they are supplied by the caller from
        /// the attributes rather than read off any discovered variant.
        /// </remarks>
        /// <param name="anchor">The method the failure is reported against.</param>
        /// <param name="argumentSetCombination">The one combination of argument set values this failure stands for.</param>
        /// <returns>The traits, keyed by trait name.</returns>
        private static Dictionary<string, IReadOnlyCollection<string>> BuildFaultTraits(XunitTestMethod anchor, IReadOnlyList<SingleFlag> argumentSetCombination)
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

            foreach (SingleFlag flag in argumentSetCombination)
            {
                Add(flag.EnumValue.GetType().Name, flag.EnumValue.ToString());
            }

            return traits;
        }

        /// <summary>
        /// Reads the fixture argument set attribute a method declares.
        /// </summary>
        /// <param name="method">The method to inspect.</param>
        /// <returns>The attribute, or <c>null</c> when the method declares none.</returns>
        private static FixtureArgumentSetsAttribute GetMethodArgumentSetsAttribute(MethodInfo method) =>
            method.GetCustomAttributes(typeof(FixtureArgumentSetsAttribute), inherit: false).SingleOrDefault() as FixtureArgumentSetsAttribute;

        /// <summary>
        /// Decides whether a class has to be expanded method by method rather than handed to xunit
        /// unchanged.
        /// </summary>
        /// <param name="method">The method to inspect.</param>
        /// <returns><c>true</c> when the method declares argument sets, or when reading them failed.</returns>
        /// <remarks>
        /// A method whose attribute cannot even be read answers <c>true</c> so that the class takes the
        /// expansion path, where the same read is repeated inside the per-method loop and its failure
        /// is reported against that one method. Swallowing the exception here rather than there is what
        /// keeps one misdeclared method from costing the whole class its tests; it is not lost, only
        /// deferred to the place that can attribute it.
        /// </remarks>
        private static bool RequiresPerMethodExpansion(MethodInfo method)
        {
            try
            {
                return GetMethodArgumentSetsAttribute(method) != null;
            }
            catch (Exception)
            {
                return true;
            }
        }

        private async ValueTask<bool> FindTestsForTypeCore(IXunitTestClass testClass, ITestFrameworkDiscoveryOptions discoveryOptions, CallbackTracker tracker)
        {
            var attribute = testClass.Class.GetCustomAttributes(typeof(FixtureArgumentSetsAttribute), inherit: false).SingleOrDefault() as FixtureArgumentSetsAttribute;

            if (attribute == null && !testClass.Methods.Any(RequiresPerMethodExpansion))
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
                // failure claiming the whole class had not run. The method's own attribute is read
                // inside the try for the same reason: a method carrying two of them, or one whose
                // constructor throws, is that method's fault and not the class's.
                bool continueDiscovery;
                try
                {
                    continueDiscovery = await FindTestsForMethodCore(testClass, method, attribute, GetMethodArgumentSetsAttribute(method), classLevelOpenParameterSets, classLevelClosedParameterSets, discoveryOptions, tracker);
                }
                catch (Exception ex) when (!ShouldRethrowRatherThanReport(ex, tracker.IsCallbackFailure(ex)))
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

                closedVariantTestMethod.ApplyArgumentSetTraits();

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
