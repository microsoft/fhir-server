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
                // the failures are reported against those xunit would treat as tests: a fault
                // reported against a property getter names nothing a reader can act on, and the
                // getter carries none of the traits a CI leg selects the class's tests by.
                //
                // One failure is reported per lost method rather than one for the class, because a
                // single failure can only carry one set of traits, and the methods of a class need
                // not agree on theirs. A failure carrying every method's traits is dropped by a leg
                // excluding any one of them; a failure carrying only the first method's is invisible
                // to a leg selecting by another's. Either way a leg passes green with tests missing.
                MethodInfo[] lostMethods = TryReadMethodsToReportAgainst(testClass);

                if (lostMethods.Length == 0)
                {
                    // Nothing to hang a test case off. Rethrowing keeps the original xunit behaviour,
                    // which is all that is left.
                    Console.WriteLine(
                        $"[FixtureArgumentSets] ERROR: discovery of '{testClass.TestClassName}' failed and it declares no method to report the failure against. {ex}");
                    throw;
                }

                return await ReportDiscoveryFault(
                    testClass,
                    lostMethods,
                    tracker,
                    ex,
                    $"Discovering the fixture argument set variants of '{testClass.TestClassName}' failed, so none of its tests ran.");
            }
        }

        /// <summary>
        /// Reads the methods a class's discovery failure should be reported against.
        /// </summary>
        /// <remarks>
        /// This runs inside the handler for a failure whose cause may be the class itself - a type
        /// that cannot be loaded, or whose members cannot be reflected over. Reading its methods can
        /// therefore throw the same way the discovery did, and an exception escaping a catch clause
        /// replaces the original with one raised while handling it, losing the only description of
        /// what actually went wrong. Returning nothing instead leaves the caller to rethrow the
        /// original, which is the outcome when there is no method to report against anyway.
        /// </remarks>
        /// <param name="testClass">The class whose discovery failed.</param>
        /// <returns>The methods to report against, empty if they could not be read.</returns>
        private static MethodInfo[] TryReadMethodsToReportAgainst(IXunitTestClass testClass)
        {
            try
            {
                return SelectMethodsToReportAgainst(testClass.Methods);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[FixtureArgumentSets] ERROR: the methods of '{testClass.TestClassName}' could not be read while reporting a discovery failure against them. {ex}");

                return Array.Empty<MethodInfo>();
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
        /// <remarks>
        /// The test is for <see cref="IFactAttribute"/>, which is what xunit itself discovers by, and
        /// not for <see cref="FactAttribute"/>. An attribute may implement the interface without
        /// deriving from that class, and such a method is every bit as much a test: matching only the
        /// class would leave it out of the methods a failure is reported against, so the tests it
        /// stands for would be missing from a run that still reported success - the one outcome this
        /// mechanism exists to prevent. Matching the interface can only widen the set.
        /// <para>
        /// This is asked while handling a discovery failure, over the very metadata that may have
        /// caused it, so a method whose attributes cannot be read answers that its test-ness is
        /// unknown rather than throwing, and rather than claiming it is not a test. Those are not the
        /// same answer: the attributes that cannot be read are very often the ones that caused the
        /// failure being reported, and answering "not a test" there would drop the method from the
        /// set a failure stands in for.
        /// </para>
        /// </remarks>
        /// <param name="method">The method to inspect.</param>
        /// <returns>
        /// <c>true</c> when the method carries a fact or theory attribute, <c>false</c> when it
        /// certainly does not, and <c>null</c> when its attributes could not be read.
        /// </returns>
        internal static bool? IsTestMethod(MethodInfo method)
        {
            try
            {
                return method.GetCustomAttributes(inherit: true).Any(attribute => attribute is IFactAttribute);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[FixtureArgumentSets] WARNING: the attributes of '{method.Name}' could not be read while choosing the methods to report a discovery failure against, so it is treated as a test. {ex}");
                return null;
            }
        }

        /// <summary>
        /// Chooses the methods a class's discovery failure is reported against.
        /// </summary>
        /// <remarks>
        /// A method whose test-ness could not be determined is kept rather than dropped. The two
        /// mistakes are not equal: reporting a failure against a helper is noise a reader can see and
        /// dismiss, while dropping a method that really was a test means no failure stands in for it,
        /// and a leg selecting by a trait only that method carried passes green with it absent.
        /// <para>
        /// Only when no method is a test, or may be one, does this fall back to whatever the class
        /// declares first, so that a failure is still reported somewhere rather than not at all. A
        /// failure reported against a property accessor names nothing a reader can act on, which is
        /// why it is the last resort rather than part of the set.
        /// </para>
        /// </remarks>
        /// <param name="methods">The methods the class declares.</param>
        /// <returns>The methods to report against.</returns>
        internal static MethodInfo[] SelectMethodsToReportAgainst(IEnumerable<MethodInfo> methods)
        {
            EnsureArg.IsNotNull(methods, nameof(methods));

            MethodInfo[] all = methods as MethodInfo[] ?? methods.ToArray();
            MethodInfo[] testMethods = all.Where(method => IsTestMethod(method) != false).ToArray();

            return testMethods.Length > 0 ? testMethods : all.Take(1).ToArray();
        }

        /// <summary>
        /// Reports a discovery failure as a test case that fails when the run reaches it.
        /// </summary>
        /// <param name="testClass">The class being discovered.</param>
        /// <param name="lostMethods">The methods the fault lost, each of which gets its own failures.</param>
        /// <param name="tracker">The callback discovered test cases are handed to.</param>
        /// <param name="exception">The failure being reported.</param>
        /// <param name="summary">A sentence saying what was lost, used to open the failure message.</param>
        /// <returns>Whether discovery should continue, as the callback reported it.</returns>
        /// <remarks>
        /// The console line written here is the record of the fault that does not depend on anything
        /// downstream. The test case is what puts the failure in the results and in the exit code, and
        /// it does normally carry the cause - see the last paragraph - but only because it is anchored
        /// to the variant class whose fixture can still be built. A case travels through the ordinary
        /// class runner, which builds the class's fixtures first, so anchoring it anywhere the
        /// fixture's argument is unavailable would aggregate a fixture failure ahead of this message
        /// and report that instead. Both ways out of that were tried and are worse - anchoring the case
        /// to a class of its own takes it out of the namespace and class filters that would have
        /// selected the tests it replaces, and discarding the aggregated failure discards this message
        /// with it, leaving the fault unreported again.
        /// <para>
        /// Every failure reported here stands for exactly one thing that would have run: one method,
        /// under one combination of argument set values. Nothing is merged, because a case carrying
        /// more than one method's traits, or more than one combination's values, is a case a filter can
        /// drop for a reason that applies to only part of what it stands for. A trait filter drops a
        /// case when <em>any</em> of its values under the named trait matches, so a single case
        /// declaring both <c>DataStore=CosmosDb</c> and <c>DataStore=SqlServer</c> is dropped by the
        /// SQL leg's <c>--filter-not-trait DataStore=CosmosDb</c> and by the Cosmos leg's
        /// <c>--filter-not-trait DataStore=SqlServer</c> alike - reported to nobody while both legs
        /// stay green. The same holds of ordinary traits: pooling one method's
        /// <c>Category=ExportLongRunning</c> into the failures standing for methods that do not
        /// declare it would let the normal leg's <c>Category!=ExportLongRunning</c> drop all of them.
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
            IReadOnlyList<MethodInfo> lostMethods,
            CallbackTracker tracker,
            Exception exception,
            string summary)
        {
            Console.WriteLine($"[FixtureArgumentSets] ERROR: {summary} Each lost test was replaced by a failing test case. {exception}");

            foreach (MethodInfo method in lostMethods)
            {
                if (!await ReportDiscoveryFaultForMethod(testClass, method, tracker, exception, summary))
                {
                    return false;
                }
            }

            return true;
        }

        private async ValueTask<bool> ReportDiscoveryFaultForMethod(
            IXunitTestClass testClass,
            MethodInfo method,
            CallbackTracker tracker,
            Exception exception,
            string summary)
        {
            // Overloads share a name, so a fault reported against each of two of them would otherwise
            // give both cases the same unique ID and xunit would keep only one.
            string methodKey = BuildFaultMethodKey(method);

            IReadOnlyList<IReadOnlyList<SingleFlag>> combinations = BuildFaultArgumentSetCombinations(ReadFaultArgumentSetDimensions(testClass, method));

            foreach (IReadOnlyList<SingleFlag> combination in combinations)
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
                    uniqueID: UniqueIDGenerator.ForTestMethod(anchorClass.UniqueID, methodKey));

                // The test case is handed to xunit through the callback, which owns it from then on
                // and disposes it with the rest of the discovered cases.
#pragma warning disable CA2000
                var errorTestCase = new DiscoveryErrorTestCase(
                    errorTestMethod,
                    $"{testClass.TestClassName}.{method.Name} (fixture argument set discovery{displaySuffix})",
                    $"{testClass.UniqueID}-{methodKey}-fixture-argument-set-discovery-error{idSuffix}",
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
        /// Closes the argument set values a failed class declared over each other, so that each
        /// combination its tests would have run as can be reported as its own failure.
        /// </summary>
        /// <param name="argumentSetDimensions">
        /// The dimensions each declared argument set attribute expands to, one entry per attribute,
        /// each holding that attribute's dimensions in the order it declares them.
        /// </param>
        /// <returns>
        /// One list per combination, deduplicated. A class declaring no argument set values yields a
        /// single empty combination, so a fault is always reported at least once.
        /// </returns>
        /// <remarks>
        /// A dimension is one flags enum, taken by one of the fixture's constructor parameters, and
        /// the variants an attribute expands to are the product of the single flags set within each of
        /// its dimensions. Reproducing that product means a leg selecting a variant by trait selects
        /// the failure standing in for it, and a leg excluding a variant by trait excludes only that
        /// one and still sees the rest.
        /// <para>
        /// Each attribute is closed over separately rather than every value being pooled into one
        /// product, because attributes need not agree on which dimensions they use. Pooling would give
        /// the failures standing in for a method that declares only a data store a format value it
        /// never asked for, and a leg excluding that format would then exclude every failure the class
        /// produced - passing green with those tests missing, which is the one outcome this whole
        /// mechanism exists to prevent. Closing each attribute separately keeps every combination one
        /// that some method really would have run as.
        /// </para>
        /// </remarks>
        internal static IReadOnlyList<IReadOnlyList<SingleFlag>> BuildFaultArgumentSetCombinations(IEnumerable<SingleFlag[][]> argumentSetDimensions)
        {
            EnsureArg.IsNotNull(argumentSetDimensions, nameof(argumentSetDimensions));

            var combinations = new List<IReadOnlyList<SingleFlag>>();

            // Two attributes declaring the same values would otherwise be reported twice, and the two
            // failures would share a unique ID.
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (SingleFlag[][] attributeDimensions in argumentSetDimensions)
            {
                IEnumerable<IEnumerable<SingleFlag>> dimensions = (attributeDimensions ?? Array.Empty<SingleFlag[]>())
                    .Where(dimension => dimension != null)
                    .Select(dimension => (IEnumerable<SingleFlag>)dimension
                        .Where(flag => flag.EnumValue != null)
                        .GroupBy(flag => flag.EnumValue)
                        .Select(values => values.First())
                        .ToArray())
                    .Where(dimension => dimension.Any())
                    .ToArray();

                foreach (IEnumerable<SingleFlag> combination in CartesianProduct(dimensions))
                {
                    SingleFlag[] closed = combination.ToArray();
                    string key = string.Join(
                        "|",
                        closed
                            .Select(flag => $"{flag.EnumValue.GetType().FullName}.{flag.EnumValue}")
                            .OrderBy(text => text, StringComparer.Ordinal));

                    if (seen.Add(key))
                    {
                        combinations.Add(closed);
                    }
                }
            }

            if (combinations.Count == 0)
            {
                combinations.Add(Array.Empty<SingleFlag>());
            }

            return combinations;
        }

        /// <summary>
        /// Reads the argument set dimensions that apply to one lost method, for use in reporting the
        /// discovery failure that stands in for it.
        /// </summary>
        /// <remarks>
        /// This runs while handling a discovery failure, and reads the same attributes whose contents
        /// may well be what caused it, so each attribute is read and expanded on its own and any
        /// further exception costs only that one attribute's values. Reporting the failure with fewer
        /// traits is worth more than not reporting it at all.
        /// <para>
        /// Every attribute the class and the method declare is read, not the single one the expansion
        /// would have used, because declaring two is one of the ways the expansion fails: asking for
        /// the single attribute throws, and answering that with no values at all leaves the failure
        /// carrying none of the traits its own declaration named. A method declaring
        /// <c>DataStore=SqlServer</c> twice over would then reach the SQL leg with no
        /// <c>DataStore</c> trait at all, and that leg selects positively on one - so it would match
        /// nothing, and pass green with the method missing.
        /// </para>
        /// <para>
        /// Only this method's own attributes are read, never a sibling's. The failure stands for this
        /// method alone, and a value borrowed from a sibling is a value no test of this method would
        /// have carried, which an exclusion filter can then use to drop the failure entirely.
        /// </para>
        /// <para>
        /// A method's own attributes are read alongside its class's rather than merged over them the
        /// way the successful path merges them. The merge cannot be trusted here - it is one of the
        /// things that may have just failed - and the two directions of error are not equal. Reading
        /// both over-reports: a method narrowing a dimension is also reported under the class's wider
        /// values, so a leg sees a failure for a variant that would not have existed. Merging and
        /// getting it wrong under-reports, and a leg then passes green with tests missing. The
        /// over-reported failure is loud and traceable to this same declaration; the under-reported
        /// one is silent, so the union is deliberate.
        /// </para>
        /// <para>
        /// Reading an attribute runs its constructor, so an attribute whose constructor throws is both
        /// a cause of a fault and, ordinarily, unreadable afterwards. The values it was given are still
        /// in the assembly's metadata, which can be read without running anything, so that is the
        /// fallback: the failure still carries the traits the lost tests would have had. This matters
        /// because the E2E and export legs select positively on an argument set, with
        /// <c>(DataStore=SqlServer)&amp;(...)</c>, and a positive filter cannot match a trait that is
        /// not there - such a leg would match nothing and report success with a class missing.
        /// </para>
        /// <para>
        /// What remains beyond reach is a declaration whose metadata cannot be read either, or whose
        /// argument sets are not enum constants in the metadata at all. The failure then carries no
        /// argument set trait and only the legs that filter by exclusion report it; the console output
        /// above still names the class either way.
        /// </para>
        /// </remarks>
        /// <param name="testClass">The class whose expansion failed.</param>
        /// <param name="method">The lost method the failure stands in for.</param>
        /// <returns>The dimensions of each declared attribute; empty when none could be read.</returns>
        private static List<SingleFlag[][]> ReadFaultArgumentSetDimensions(IXunitTestClass testClass, MethodInfo method)
        {
            var dimensions = new List<SingleFlag[][]>();

            AddAll(
                () => testClass.Class.GetCustomAttributes(typeof(FixtureArgumentSetsAttribute), inherit: false),
                () => testClass.Class.GetCustomAttributesData(),
                testClass.TestClassName);
            AddAll(
                () => method.GetCustomAttributes(typeof(FixtureArgumentSetsAttribute), inherit: false),
                () => method.GetCustomAttributesData(),
                $"{testClass.TestClassName}.{method.Name}");

            return dimensions;

            void AddAll(Func<object[]> read, Func<IEnumerable<CustomAttributeData>> readMetadata, string owner)
            {
                object[] attributes;
                try
                {
                    attributes = read();
                }
                catch (Exception ex)
                {
                    // Reading an attribute runs its constructor, and a constructor that throws is one
                    // of the ways a class gets here in the first place. The values are still in the
                    // assembly's metadata, which can be read without running anything, so the failure
                    // standing in for these tests can still carry the traits a leg selects on. Without
                    // this the failure would reach the runner bare, and the export and E2E legs select
                    // positively on a data store, so they would match nothing and report success.
                    Console.WriteLine(
                        $"[FixtureArgumentSets] WARNING: the argument set attributes of '{owner}' could not be read, so their values are being taken from metadata instead. {ex}");

                    AddAllFromMetadata(readMetadata, owner);
                    return;
                }

                // Each attribute is expanded on its own so that the one whose contents may have caused
                // the fault in the first place costs only its own values, rather than leaving the
                // failure with no traits for a leg's filter to select it by.
                foreach (FixtureArgumentSetsAttribute attribute in attributes.OfType<FixtureArgumentSetsAttribute>())
                {
                    try
                    {
                        dimensions.Add(ExpandFaultEnumFlagsFromAttributeData(attribute));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[FixtureArgumentSets] WARNING: an argument set attribute of '{owner}' could not be expanded, so a trait filter may not select the failure standing in for its tests. {ex}");
                    }
                }
            }

            void AddAllFromMetadata(Func<IEnumerable<CustomAttributeData>> readMetadata, string owner)
            {
                IEnumerable<CustomAttributeData> attributeData;
                try
                {
                    attributeData = readMetadata()
                        .Where(d => typeof(FixtureArgumentSetsAttribute).IsAssignableFrom(d.AttributeType))
                        .ToArray();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[FixtureArgumentSets] WARNING: the argument set metadata of '{owner}' could not be read either, so a trait filter may not select the failure standing in for its tests. {ex}");
                    return;
                }

                foreach (CustomAttributeData data in attributeData)
                {
                    try
                    {
                        dimensions.Add(ExpandEnumFlagsFromMetadata(data));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[FixtureArgumentSets] WARNING: an argument set attribute of '{owner}' could not be expanded from metadata, so a trait filter may not select the failure standing in for its tests. {ex}");
                    }
                }
            }
        }

        /// <summary>
        /// Expands the argument sets an attribute declares by reading the assembly's metadata, without
        /// constructing the attribute.
        /// </summary>
        /// <remarks>
        /// This is the fault path's way round an attribute whose constructor throws. It reads the same
        /// enum values the constructor would have been handed, so the dimensions it produces are the
        /// ones the tests would have carried had the class expanded.
        /// </remarks>
        /// <param name="attributeData">The metadata of one argument set attribute.</param>
        /// <returns>One array of single-valued flags per declared dimension.</returns>
        private static SingleFlag[][] ExpandEnumFlagsFromMetadata(CustomAttributeData attributeData)
        {
            var dimensions = new List<SingleFlag[]>();
            var enumTypes = new List<Type>();

            foreach (CustomAttributeTypedArgument argument in attributeData.ConstructorArguments)
            {
                // A derived attribute names its enums one per parameter, while the base takes them as
                // a params array, so an argument is either an enum or a collection of them.
                if (argument.Value is IReadOnlyCollection<CustomAttributeTypedArgument> elements)
                {
                    foreach (CustomAttributeTypedArgument element in elements)
                    {
                        AddDimension(element);
                    }
                }
                else
                {
                    AddDimension(argument);
                }
            }

            // This path only ever runs while reporting a fault, so a declaration naming nothing is
            // widened here for the same reason it is on the reflection path.
            return WidenFaultDimensionsThatNameNothing(dimensions.ToArray(), enumTypes.ToArray());

            void AddDimension(CustomAttributeTypedArgument argument)
            {
                Type argumentType = argument.ArgumentType;

                // Metadata stores an enum as its underlying value, so the declared type is what says
                // which enum it was. Anything that is not an enum is not a dimension.
                if (argumentType == null || !argumentType.IsEnum || argument.Value == null)
                {
                    return;
                }

                dimensions.Add(GetSingleValuedFlags((Enum)Enum.ToObject(argumentType, argument.Value)).ToArray());
                enumTypes.Add(argumentType);
            }
        }

        /// <summary>
        /// Collects the traits a failure standing in for a method's tests must carry to be selected by
        /// the same filters they would have been.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two kinds of trait matter. The ordinary ones the method and its class declare come from the
        /// anchoring method itself, and CI legs combine them with argument set values in filters such
        /// as <c>(DataStore=CosmosDb)&amp;(Category=ExportLongRunning)</c>, so leaving either out lets
        /// the leg that would have run these tests pass without them. The argument set values are the
        /// ones the expansion never got far enough to produce, so they are supplied by the caller from
        /// the attributes rather than read off any discovered variant.
        /// </para>
        /// <para>
        /// Reading the anchor's own traits runs xunit's trait discovery, which evaluates user code and
        /// the method's and class's attributes, and so can fail for the same reason the expansion did.
        /// That is caught here: a failure reported with only its argument set traits is still reported,
        /// whereas letting the read escape would take the whole mechanism down with it and leave the
        /// class silently absent from a green run.
        /// </para>
        /// </remarks>
        /// <param name="anchor">The method the failure is reported against.</param>
        /// <param name="argumentSetCombination">The one combination of argument set values this failure stands for.</param>
        /// <returns>The traits, keyed by trait name.</returns>
        private static Dictionary<string, IReadOnlyCollection<string>> BuildFaultTraits(
            XunitTestMethod anchor,
            IReadOnlyList<SingleFlag> argumentSetCombination)
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

            try
            {
                foreach (KeyValuePair<string, IReadOnlyCollection<string>> trait in anchor.Traits)
                {
                    foreach (string value in trait.Value)
                    {
                        Add(trait.Key, value);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[FixtureArgumentSets] WARNING: the ordinary traits of '{anchor.TestClass.TestClassName}.{anchor.MethodName}' could not be read in one go, so they are being read one attribute at a time. {ex}");

                AddFaultTraitsPerAttribute(anchor, Add);
            }

            foreach (SingleFlag flag in argumentSetCombination)
            {
                Add(flag.EnumValue.GetType().Name, flag.EnumValue.ToString());
            }

            return traits;
        }

        /// <summary>
        /// Reads the ordinary traits of the method a discovery failure stands for one attribute at a
        /// time, so that a single attribute refusing to produce its traits costs only its own.
        /// </summary>
        /// <remarks>
        /// xUnit computes a test method's traits on demand and returns them as one dictionary, so
        /// asking for them runs every trait attribute the method and its class declare. One that
        /// throws - a trait computed from configuration that is not present, say - therefore takes
        /// the whole dictionary with it, leaving the failure carrying none of the traits its other
        /// attributes named. That is the same silence this fault path exists to break: the export
        /// leg selects positively with <c>(DataStore=X)&amp;(Category=Y)</c>, and a filter cannot
        /// match a trait that is absent, so the leg would report success with the method missing.
        /// <para>
        /// This is only the fallback. The ordinary read is tried first and is what runs for a
        /// healthy class, because it is xUnit's own and needs no agreement with it about how traits
        /// are gathered.
        /// </para>
        /// </remarks>
        /// <param name="anchor">The method the failure is reported against.</param>
        /// <param name="add">Records one trait name and value.</param>
        private static void AddFaultTraitsPerAttribute(XunitTestMethod anchor, Action<string, string> add)
        {
            foreach (MemberInfo declaration in new MemberInfo[] { anchor.TestClass?.Class, anchor.Method })
            {
                if (declaration == null)
                {
                    continue;
                }

                ITraitAttribute[] traitAttributes;

                try
                {
                    traitAttributes = declaration.GetCustomAttributes(inherit: true).OfType<ITraitAttribute>().ToArray();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[FixtureArgumentSets] WARNING: the trait attributes of '{declaration.Name}' could not be read, so a trait filter may not select the failure standing in for its tests. {ex}");
                    continue;
                }

                foreach (ITraitAttribute traitAttribute in traitAttributes)
                {
                    try
                    {
                        foreach (KeyValuePair<string, string> trait in traitAttribute.GetTraits())
                        {
                            add(trait.Key, trait.Value);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[FixtureArgumentSets] WARNING: the trait attribute '{traitAttribute.GetType().Name}' of '{declaration.Name}' could not produce its traits, so a filter naming those traits may not select the failure standing in for its tests. {ex}");
                    }
                }
            }
        }

        /// <summary>
        /// Builds the part of a discovery failure's unique ID that identifies the method it is
        /// reported against.
        /// </summary>
        /// <remarks>
        /// The method's name alone is not unique: a class may overload a test method, and two failures
        /// sharing a unique ID leave xunit reporting only one of them - the other silently absent,
        /// which is the outcome reporting these failures exists to prevent. The parameter types and
        /// the generic arity are included to tell overloads apart - overloads may differ by arity
        /// alone, as <c>Run&lt;T&gt;()</c> and <c>Run&lt;T, U&gt;()</c> do, and both take no parameters
        /// - and are read defensively because this runs while handling a failure that may itself be a
        /// type that cannot be loaded.
        /// </remarks>
        /// <param name="method">The method the failure is reported against.</param>
        /// <returns>A key identifying the method, falling back to its name alone.</returns>
        internal static string BuildFaultMethodKey(MethodInfo method)
        {
            try
            {
                string name = method.IsGenericMethodDefinition
                    ? $"{method.Name}`{method.GetGenericArguments().Length}"
                    : method.Name;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 0)
                {
                    return name;
                }

                return $"{name}({string.Join(",", parameters.Select(parameter => parameter.ParameterType.FullName ?? parameter.ParameterType.Name))})";
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[FixtureArgumentSets] WARNING: the signature of '{method.Name}' could not be read, so its failure is identified by metadata token instead. {ex}");

                try
                {
                    // The token distinguishes overloads within an assembly without reading any type
                    // the failure may have been caused by being unable to load. Falling back to the
                    // name alone would let two overloads share a unique ID, and xunit reports only
                    // one of a colliding pair - losing the very failure this is reporting.
                    return $"{method.Name}#{method.MetadataToken}";
                }
                catch (Exception tokenException)
                {
                    Console.WriteLine(
                        $"[FixtureArgumentSets] WARNING: the metadata token of '{method.Name}' could not be read either, so a failure reported against an overload of it may be dropped as a duplicate. {tokenException}");
                    return method.Name;
                }
            }
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
                        new[] { method },
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

                // The merge runs over every dimension either side declares, not just the ones the
                // method does. A method carrying an attribute with fewer dimensions than its class -
                // which happens when the two carry different attribute types - would otherwise have
                // the class's trailing dimensions dropped rather than inherited, and its tests would
                // then run without the values, and so without the traits, that a CI leg selects them
                // by: the leg would pass with those tests silently absent.
                int dimensionCount = Math.Max(methodLevelOpenParameterSets.Length, classLevelOpenParameterSets.Length);
                var mergedOpenParameterSets = new SingleFlag[dimensionCount][];

                bool hasOverride = false;
                for (int i = 0; i < dimensionCount; i++)
                {
                    SingleFlag[] methodLevelDimension = i < methodLevelOpenParameterSets.Length ? methodLevelOpenParameterSets[i] : null;

                    if (methodLevelDimension?.Length > 0)
                    {
                        SingleFlag[] classLevelDimension = i < classLevelOpenParameterSets.Length ? classLevelOpenParameterSets[i] : null;
                        if (classLevelDimension?.Length > 0)
                        {
                            Type methodLevelType = methodLevelDimension[0].EnumValue?.GetType();
                            Type classLevelType = classLevelDimension[0].EnumValue?.GetType();

                            if (methodLevelType != classLevelType)
                            {
                                // Dimensions are matched by position, but the fixture's constructor
                                // arguments are matched by enum type. A method whose attribute takes a
                                // different enum in this position is therefore not overriding the
                                // class's dimension but replacing it with an unrelated one, and the
                                // variants would carry no value - and so no trait - for the dimension
                                // the class declared. A leg selecting on it would then run none of
                                // this method's tests and still pass. Only the convention that a class
                                // and its methods use argument set attributes taking the same enums in
                                // the same order keeps that from happening, so it is checked rather
                                // than assumed.
                                throw new InvalidOperationException(
                                    $"'{testClass.TestClassName}.{method.Name}' declares '{methodLevelType}' where its class declares '{classLevelType}' in argument set dimension {i}. " +
                                    "A method-level fixture argument set attribute must take the same enums, in the same order, as the class-level one it overrides.");
                            }
                        }

                        hasOverride = true;
                        mergedOpenParameterSets[i] = methodLevelDimension;
                    }
                    else
                    {
                        // Take the class-level set: this position is either one the method left empty
                        // or one it says nothing about at all. A method that declares more dimensions
                        // than its class and leaves the extra one empty is asking to inherit a
                        // dimension that does not exist, so there is nothing to take. Reading past the
                        // end would throw too, but with a message naming an array index rather than the
                        // attribute that is wrong, and this exception is what the developer is shown as
                        // the body of the failing test standing in for the method.
                        if (i >= classLevelOpenParameterSets.Length)
                        {
                            throw new InvalidOperationException(
                                $"'{testClass.TestClassName}.{method.Name}' declares {methodLevelOpenParameterSets.Length} fixture argument set dimensions where its class declares {classLevelOpenParameterSets.Length}, and names no value in dimension {i}. " +
                                "A dimension naming no value inherits the class-level one in that position, and the class has no dimension there. " +
                                "Either name the values that dimension should take, or drop it so the method declares no more dimensions than its class.");
                        }

                        // That throw is caught by the per-method handler in FindTestsForTypeCore and
                        // reported as a failing test case standing in for this method, rather than
                        // losing the method quietly.
                        mergedOpenParameterSets[i] = classLevelOpenParameterSets[i];
                    }
                }

                if (hasOverride)
                {
                    // convert to the form (Arg1.OptionA, Arg2.OptionA), (Arg1.OptionA, Arg2.OptionB), (Arg1.OptionB, Arg2.OptionA), (Arg1.OptionB, Arg2.OptionB)
                    closedSets = CartesianProduct(mergedOpenParameterSets).Select(e => e.ToArray()).ToArray();
                }
            }

            if (closedSets.Length == 0)
            {
                // Reaching here means an argument set was declared, so a product of nothing is a
                // misconfiguration -- an argument set of zero, a value naming no single flag, or a
                // null entry -- and every test on this method would otherwise be absent from a run
                // that still reported success. Throwing hands it to the per-method handler in
                // FindTestsForTypeCore, which reports it as a failing test case so it shows up in
                // the results and in the exit code. Discovery over every test assembly in this
                // repository produces no such case, so this only fires on a class that is genuinely
                // misdeclared.
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
                var variantMethod = closedVariantTestMethod;
                Func<ITestCase, ValueTask<bool>> variantCallback = testCase =>
                {
                    ApplyVariantDisplayName(testCase, variantArguments);
                    ApplyVariantTraits(testCase, variantMethod);
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
                // Without the suffix every variant of this method reports under the same name, so a
                // failure cannot be attributed to a fixture argument set. Every test case type
                // xunit.v3 produces derives from XunitTestCase, so this only happens if a custom
                // discoverer introduces its own type. Fail rather than carry on: the per-method
                // handler turns this into a reported failure per fixture argument set, which is
                // visible to a filtering leg, whereas a warning on stdout is not.
                throw new InvalidOperationException(
                    $"Test case '{testCase.TestCaseDisplayName}' is {testCase.GetType().Name}, not {nameof(XunitTestCase)}, " +
                    "so its fixture argument set can be applied to neither its display name nor its traits. " +
                    "Variants of this test would be indistinguishable in results and invisible to any CI leg selecting on a trait.");
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
        /// Copies the variant's traits onto a discovered test case that did not get them.
        /// </summary>
        /// <remarks>
        /// A test case built from a method normally carries that method's traits, so for a healthy
        /// test this adds nothing. The case that matters is the one xunit builds for a test it cannot
        /// run - a fact declaring parameters, a theory with no data - which it reports as an
        /// <c>ExecutionErrorTestCase</c> constructed with no traits at all. Without them that failure
        /// is invisible to any leg selecting on a trait: this repository's E2E and export legs each
        /// require a positive <c>DataStore</c>, and a filter cannot match a trait that is absent. The
        /// malformed test would then be missing from those legs and they would report success, which
        /// is precisely the outcome the rest of this class exists to prevent.
        /// <para>
        /// Traits are merged rather than replaced so that anything the discovery already attached -
        /// a theory row's own traits, say - survives.
        /// </para>
        /// </remarks>
        /// <param name="testCase">The discovered test case.</param>
        /// <param name="variantMethod">The variant method the test case was discovered from.</param>
        private static void ApplyVariantTraits(ITestCase testCase, FixtureArgumentSetTestMethod variantMethod)
        {
            if (testCase is not XunitTestCase xunitTestCase)
            {
                // A case whose traits cannot be written is one a filtering leg cannot see, which is
                // the failure this class exists to prevent -- so fail discovery rather than let it
                // through untagged. ApplyVariantDisplayName rejects the same case types, so in the
                // current call order this is unreachable; it is kept because this method needs the
                // guarantee on its own account, not because of the order it happens to be called in.
                throw new InvalidOperationException(
                    $"The fixture argument set traits of '{testCase.TestCaseDisplayName}' could not be applied because it is " +
                    $"{testCase.GetType().Name}, not {nameof(XunitTestCase)}. A CI leg selecting on those traits would not run it.");
            }

            foreach (KeyValuePair<string, IReadOnlyCollection<string>> trait in variantMethod.Traits)
            {
                if (!xunitTestCase.Traits.TryGetValue(trait.Key, out HashSet<string> values))
                {
                    values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    xunitTestCase.Traits[trait.Key] = values;
                }

                foreach (string value in trait.Value)
                {
                    values.Add(value);
                }
            }
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
            return attribute.GetArgumentSets()
                .Select(e => GetSingleValuedFlags(e).ToArray())
                .ToArray();
        }

        /// <summary>
        /// Expands an argument set declaration for the fault path, widening a dimension that names no
        /// value to every value its type declares.
        /// </summary>
        /// <remarks>
        /// A dimension naming no value expands to nothing, so the product of the dimensions is empty
        /// and the failure standing in for the method carries no argument set trait at all. The E2E
        /// and export legs select positively on that trait, so such a failure is invisible to them:
        /// the leg reports success with the method's tests simply absent, which is the outcome this
        /// whole path exists to prevent. The value is unknown but the type is not - a dimension is
        /// declared as an enum whether or not it names any of its flags - so the failure is reported
        /// once per value that type declares instead. That over-reports, in the same deliberate
        /// direction as the rest of this path: a leg seeing a failure for a variant that would not
        /// have existed is loud and traceable to this declaration, where a leg seeing nothing is not.
        /// </remarks>
        /// <param name="attribute">The declaration to expand.</param>
        /// <returns>One dimension per argument set, each holding at least one value where the type is known.</returns>
        private static SingleFlag[][] ExpandFaultEnumFlagsFromAttributeData(FixtureArgumentSetsAttribute attribute)
        {
            IReadOnlyList<Enum> argumentSets = attribute.GetArgumentSets();
            SingleFlag[][] dimensions = argumentSets
                .Select(e => GetSingleValuedFlags(e).ToArray())
                .ToArray();

            return WidenFaultDimensionsThatNameNothing(dimensions, argumentSets.Select(e => e?.GetType()).ToArray());
        }

        /// <summary>
        /// Replaces the dimensions of a declaration that names no value at all with every value their
        /// types declare, so the failure standing in for the method can still be selected.
        /// </summary>
        /// <remarks>
        /// This fires only when the declaration produces no combination whatsoever. A dimension that
        /// names nothing alongside one that does is not this case: naming nothing in one position is
        /// how a method asks to inherit that position from its class, and widening it would report
        /// the failure under values the declaration never asked for.
        /// </remarks>
        /// <param name="dimensions">The dimensions as declared.</param>
        /// <param name="enumTypes">The type of each dimension, positionally, with null where unknown.</param>
        /// <returns>The dimensions to report the failure against.</returns>
        private static SingleFlag[][] WidenFaultDimensionsThatNameNothing(SingleFlag[][] dimensions, Type[] enumTypes)
        {
            if (dimensions.Length == 0 || dimensions.Any(dimension => dimension.Length > 0))
            {
                return dimensions;
            }

            return dimensions
                .Select((dimension, index) => AllSingleValuedFlagsOf(index < enumTypes.Length ? enumTypes[index] : null))
                .ToArray();
        }

        /// <summary>
        /// Lists every single-valued flag an enum type declares, for reporting a failure whose own
        /// declaration named none of them.
        /// </summary>
        /// <param name="enumType">The type to read, which may be null or not an enum.</param>
        /// <returns>The type's single-valued flags, or nothing if they cannot be read.</returns>
        private static SingleFlag[] AllSingleValuedFlagsOf(Type enumType)
        {
            if (enumType == null || !enumType.IsEnum)
            {
                return Array.Empty<SingleFlag>();
            }

            try
            {
                return Enum.GetValues(enumType)
                    .Cast<Enum>()
                    .Where(value =>
                    {
                        long asLong = Convert.ToInt64(value);
                        return (asLong != 0) && ((asLong & (asLong - 1)) == 0);
                    })
                    .Select(value => new SingleFlag(value))
                    .ToArray();
            }
            catch (Exception ex)
            {
                // Reading the type's values is the only thing that can fail here, and a failure leaves
                // the dimension as it was: the failure is still reported, just without this trait.
                // Losing the trait is the thing being guarded against, so it is said out loud.
                Console.WriteLine(
                    $"[FixtureArgumentSets] WARNING: '{enumType.FullName}' names no value in this declaration and its values could not be read, so the failure reported in the method's place carries no trait for that dimension and a leg selecting positively on it will not see the failure. {ex}");

                return Array.Empty<SingleFlag>();
            }
        }

        /// <summary>
        /// Splits a flags enum value into the single-valued flags it names.
        /// </summary>
        /// <param name="e">The value to split, which may be null.</param>
        /// <returns>One <see cref="SingleFlag"/> per flag set in the value.</returns>
        private static IEnumerable<SingleFlag> GetSingleValuedFlags(Enum e)
        {
            bool IsPowerOfTwo(long x)
            {
                return (x != 0) && ((x & (x - 1)) == 0);
            }

            if (e is null)
            {
                yield break;
            }

            var enumAsLong = Convert.ToInt64(e);

            foreach (Enum value in Enum.GetValues(e.GetType()))
            {
                var flagAsLong = Convert.ToInt64(value);
                if (IsPowerOfTwo(flagAsLong) && (enumAsLong & flagAsLong) != 0)
                {
                    yield return new SingleFlag(value);
                }
            }
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
