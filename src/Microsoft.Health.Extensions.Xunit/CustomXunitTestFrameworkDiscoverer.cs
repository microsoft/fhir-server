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
using Xunit.Sdk;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// An <see cref="XunitTestFrameworkDiscoverer"/> that expands a fixtured test class into one test class per
    /// <c>(DataStore, Format)</c> variant.
    /// </summary>
    /// <remarks>
    /// The discoverer computes the closed set of variants for each fixtured class/method, delegates to
    /// <see cref="XunitTestFrameworkDiscoverer.FindTestsForMethod"/> so <c>[Theory]</c> rows and attribute traits are fully
    /// formed, then patches each resulting case with a per-variant class identity, a v2-parity display name, and additive
    /// variant traits.
    /// </remarks>
    internal sealed class CustomXunitTestFrameworkDiscoverer : XunitTestFrameworkDiscoverer
    {
        // Reflection point #1: the display name is stored in a private field with no public setter, so the synthetic
        // (DataStore, Format) suffix is injected here. Pinned to xunit.v3 3.2.2; fails loudly if the field is gone.
        private static readonly FieldInfo DisplayNameField =
            typeof(XunitTestCase).GetField("testCaseDisplayName", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("XunitTestCase.testCaseDisplayName (private) was not found. The xunit.v3 3.2.2 pin has changed; the variant display name cannot be set.");

        private readonly ConcurrentDictionary<string, FixtureArgumentSetTestClass> _classCache = new(StringComparer.Ordinal);

        public CustomXunitTestFrameworkDiscoverer(Assembly assembly, IXunitTestCollectionFactory collectionFactory = null)
            : base(new XunitTestAssembly(assembly, null, assembly.GetName().Version, UniqueIDGenerator.ForAssembly(assembly.Location, null)), collectionFactory)
        {
        }

        /// <inheritdoc/>
        protected override async ValueTask<bool> FindTestsForType(IXunitTestClass testClass, ITestFrameworkDiscoveryOptions options, Func<ITestCase, ValueTask<bool>> callback)
        {
            FixtureArgumentSetsAttribute classAttribute;
            SingleFlag[][] classOpenSets;
            SingleFlag[][] classClosedSets;
            try
            {
                classAttribute = testClass.Class.GetCustomAttributes(typeof(FixtureArgumentSetsAttribute), false).SingleOrDefault() as FixtureArgumentSetsAttribute;

                // IsDefined detects presence without instantiating the attribute, so a single method whose attribute
                // throws on construction does not take down the whole class here; it is isolated to its own per-method
                // try below.
                bool anyMethodAttribute = testClass.Methods.Any(m => m.IsDefined(typeof(FixtureArgumentSetsAttribute), false));
                if (classAttribute == null && !anyMethodAttribute)
                {
                    return await base.FindTestsForType(testClass, options, callback);
                }

                classOpenSets = classAttribute == null ? Array.Empty<SingleFlag[]>() : Expand(classAttribute);
                classClosedSets = classOpenSets.Length == 0 ? Array.Empty<SingleFlag[]>() : CartesianProduct(classOpenSets);
            }
            catch (Exception ex)
            {
                // A throw out of discovery is silently dropped by xUnit v3: the class vanishes, the run reports fewer
                // tests, and it still exits 0. Emit a failing case per method so the failure lands in the results and the
                // exit code. Exact variants may be unavailable here, but ReportFault attaches a best-effort union of raw
                // class/method flags so positive trait filters can still retain the failure.
                return await ReportFault(testClass, TryGetMethods(testClass), null, ex, callback);
            }

            foreach (MethodInfo method in testClass.Methods)
            {
                FixtureArgumentSetsAttribute methodAttribute = null;
                bool succeeded;
                try
                {
                    // Inside the try: a method whose attribute throws on construction becomes a loud fault case for that
                    // one method instead of escaping FindTestsForType (which v3 swallows, silently dropping the method).
                    methodAttribute = method.GetCustomAttributes(typeof(FixtureArgumentSetsAttribute), false).SingleOrDefault() as FixtureArgumentSetsAttribute;
                    succeeded = await ExpandMethod(testClass, method, classAttribute, methodAttribute, classOpenSets, classClosedSets, options, callback);
                }
                catch (Exception ex)
                {
                    // Per-method isolation: one method's failure must not drop the rest of the class. Re-derive this
                    // method's variants (best effort) so the fault carries their traits and a trait-filtered CI leg still
                    // selects it.
                    SingleFlag[][] variants = TryComputeVariants(testClass, method, classAttribute, methodAttribute, classOpenSets, classClosedSets);
                    succeeded = await ReportFault(testClass, new[] { method }, variants, ex, callback);
                }

                if (!succeeded)
                {
                    return false;
                }
            }

            return true;
        }

        private async ValueTask<bool> ExpandMethod(
            IXunitTestClass testClass,
            MethodInfo method,
            FixtureArgumentSetsAttribute classAttribute,
            FixtureArgumentSetsAttribute methodAttribute,
            SingleFlag[][] classOpenSets,
            SingleFlag[][] classClosedSets,
            ITestFrameworkDiscoveryOptions options,
            Func<ITestCase, ValueTask<bool>> callback)
        {
            if (classAttribute == null && methodAttribute == null)
            {
                var passthrough = new XunitTestMethod(testClass, method, Array.Empty<object>(), UniqueIDGenerator.ForTestMethod(testClass.UniqueID, method.Name));
                return await FindTestsForMethod(passthrough, options, callback);
            }

            SingleFlag[][] closedSets = ComputeVariants(testClass, method, classAttribute, methodAttribute, classOpenSets, classClosedSets);

            if (closedSets.Length == 0)
            {
                throw new InvalidOperationException($"'{testClass.TestClassName}.{method.Name}' expanded to no fixture argument sets, so none of its tests would run.");
            }

            foreach (SingleFlag[] variant in closedSets)
            {
                FixtureArgumentSetTestClass variantClass = GetVariantClass(testClass, variant);
                var variantMethod = new XunitTestMethod(variantClass, method, Array.Empty<object>(), UniqueIDGenerator.ForTestMethod(variantClass.UniqueID, method.Name));

                Func<ITestCase, ValueTask<bool>> variantCallback = testCase =>
                {
                    ApplyMethodTraits(testCase, variantMethod);
                    ApplyVariantDisplayName(testCase, variant);
                    return callback(testCase);
                };

                if (!await FindTestsForMethod(variantMethod, options, variantCallback))
                {
                    return false;
                }
            }

            return true;
        }

        // Per-dimension replace: a method-level attribute replaces the class-level values for the dimension(s) it names,
        // dimension by dimension; a dimension the method omits inherits the class-level values. A method value replaces
        // (it does not intersect) the class value - e.g. a method declaring SqlServer on a CosmosDb class yields
        // (SqlServer, ...), never empty. This matches xunit v2 and is verified by an A/B of discovered test names.
        private static SingleFlag[][] ComputeVariants(
            IXunitTestClass testClass,
            MethodInfo method,
            FixtureArgumentSetsAttribute classAttribute,
            FixtureArgumentSetsAttribute methodAttribute,
            SingleFlag[][] classOpenSets,
            SingleFlag[][] classClosedSets)
        {
            if (classAttribute == null)
            {
                return CartesianProduct(Expand(methodAttribute));
            }

            if (methodAttribute == null)
            {
                return classClosedSets;
            }

            SingleFlag[][] methodOpenSets = Expand(methodAttribute);
            int dimensions = Math.Max(methodOpenSets.Length, classOpenSets.Length);
            var merged = new SingleFlag[dimensions][];
            bool overridden = false;
            for (int i = 0; i < dimensions; i++)
            {
                SingleFlag[] methodDimension = i < methodOpenSets.Length ? methodOpenSets[i] : null;
                if (methodDimension?.Length > 0)
                {
                    SingleFlag[] classDimension = i < classOpenSets.Length ? classOpenSets[i] : null;
                    if (classDimension?.Length > 0 && methodDimension[0].EnumValue?.GetType() != classDimension[0].EnumValue?.GetType())
                    {
                        throw new InvalidOperationException($"'{testClass.TestClassName}.{method.Name}' overrides dimension {i} with a different enum type than its class.");
                    }

                    merged[i] = methodDimension;
                    overridden = true;
                }
                else
                {
                    if (i >= classOpenSets.Length)
                    {
                        throw new InvalidOperationException($"'{testClass.TestClassName}.{method.Name}' names no value in dimension {i} and the class has none to inherit.");
                    }

                    merged[i] = classOpenSets[i];
                }
            }

            return overridden ? CartesianProduct(merged) : classClosedSets;
        }

        private FixtureArgumentSetTestClass GetVariantClass(IXunitTestClass testClass, SingleFlag[] variant)
        {
            string classKey = $"{testClass.TestCollection.UniqueID}|{string.Join(",", variant.Select(v => v.EnumValue.GetType().AssemblyQualifiedName + "=" + Convert.ToInt64(v.EnumValue)))}|{testClass.Class.AssemblyQualifiedName}";
            return _classCache.GetOrAdd(
                classKey,
                _ => new FixtureArgumentSetTestClass(testClass.Class, testClass.TestCollection, variant, UniqueIDGenerator.ForTestClass(testClass.TestCollection.UniqueID, classKey)));
        }

        // Emit one execution-error case per (method, variant) so a discovery failure fails loudly and carries the variant
        // traits. The fault path deliberately builds cases from the base XunitTestMethod and attaches best-effort raw
        // trait values; it never re-enters FixtureArgumentSetTestClass, whose reflection
        // point may be what failed when a fault needs reporting. A fault handler that re-triggered that same failure
        // would throw out of discovery, which v3 swallows - the class would vanish and the run would still exit 0.
        private static async ValueTask<bool> ReportFault(IXunitTestClass testClass, IEnumerable<MethodInfo> methods, SingleFlag[][] variants, Exception ex, Func<ITestCase, ValueTask<bool>> callback)
        {
            foreach (MethodInfo method in methods)
            {
                if (variants is { Length: > 0 })
                {
                    foreach (SingleFlag[] variant in variants)
                    {
                        if (!await EmitFaultCase(testClass, method, variant, variant, ex, callback))
                        {
                            return false;
                        }
                    }
                }
                else
                {
                    // The exact variants could not be computed. Attach a conservative union of the raw class/method flag
                    // values so the fault still carries DataStore/Format traits and stays visible to a positive filter
                    // (e.g. /[(DataStore=CosmosDb)]). Exact variants may be unavailable, but this best-effort union keeps
                    // the failure visible to positive trait filters instead of dropping it into a green run.
                    if (!await EmitFaultCase(testClass, method, null, TryGetRawFlags(testClass, method), ex, callback))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static async ValueTask<bool> EmitFaultCase(IXunitTestClass testClass, MethodInfo method, SingleFlag[] nameVariant, IEnumerable<SingleFlag> traitFlags, Exception ex, Func<ITestCase, ValueTask<bool>> callback)
        {
            bool named = nameVariant is { Length: > 0 };
            string suffix = named ? $"({string.Join(", ", nameVariant.Select(v => v.EnumValue))})" : string.Empty;
            string discriminator = named ? "-" + string.Join("-", nameVariant.Select(v => Convert.ToInt64(v.EnumValue))) : string.Empty;
            var faultMethod = new XunitTestMethod(testClass, method, Array.Empty<object>(), UniqueIDGenerator.ForTestMethod(testClass.UniqueID, method.Name + discriminator));
            string name = $"{testClass.TestClassName}{suffix}.{method.Name}";
            var errorCase = new ExecutionErrorTestCase(faultMethod, name, $"{faultMethod.UniqueID}-fault", sourceFilePath: null, sourceLineNumber: null, errorMessage: $"Discovering '{testClass.TestClassName}.{method.Name}' failed, so none of its tests ran: {ex.Message}");
            ApplyFlagTraits(errorCase, traitFlags);
            return await callback(errorCase);
        }

        // Attach (DataStore, Format) traits derived directly from flag values. ExecutionErrorTestCase does NOT inherit its
        // method's traits, so without this a fault case drops out of every trait-filtered CI leg and the failure is
        // invisible. Sourced from raw flags so it never touches the reflected variant types. Load-bearing - do not remove.
        private static void ApplyFlagTraits(ITestCase testCase, IEnumerable<SingleFlag> flags)
        {
            if (testCase is not XunitTestCase xunitTestCase)
            {
                return;
            }

            foreach (SingleFlag flag in flags)
            {
                string key = flag.EnumValue.GetType().Name;
                if (!xunitTestCase.Traits.TryGetValue(key, out HashSet<string> values))
                {
                    xunitTestCase.Traits[key] = values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                values.Add(flag.EnumValue.ToString());
            }
        }

        // Best-effort union of the raw class/method flags used when exact Cartesian variants cannot be computed.
        private static List<SingleFlag> TryGetRawFlags(IXunitTestClass testClass, MethodInfo method)
        {
            var flags = new List<SingleFlag>();
            CollectRawFlags(flags, () => testClass.Class);
            CollectRawFlags(flags, () => method);
            return flags;
        }

        private static void CollectRawFlags(List<SingleFlag> flags, Func<MemberInfo> member)
        {
            try
            {
                FixtureArgumentSetsAttribute attribute = member()?.GetCustomAttributes(typeof(FixtureArgumentSetsAttribute), false).SingleOrDefault() as FixtureArgumentSetsAttribute;
                if (attribute != null)
                {
                    flags.AddRange(attribute.GetArgumentSets().Where(s => s != null).SelectMany(GetSingleValuedFlags));
                }
            }
            catch
            {
                // Attribute constructor throws make the flags unknowable, but discovery can continue with the rest.
            }
        }

        // Best-effort method list: if xUnit cannot enumerate visible members, the helper returns an empty array.
        private static MethodInfo[] TryGetMethods(IXunitTestClass testClass)
        {
            try
            {
                return testClass.Methods.ToArray();
            }
            catch
            {
                return Array.Empty<MethodInfo>();
            }
        }

        private static SingleFlag[][] TryComputeVariants(IXunitTestClass testClass, MethodInfo method, FixtureArgumentSetsAttribute classAttribute, FixtureArgumentSetsAttribute methodAttribute, SingleFlag[][] classOpenSets, SingleFlag[][] classClosedSets)
        {
            try
            {
                return ComputeVariants(testClass, method, classAttribute, methodAttribute, classOpenSets, classClosedSets);
            }
            catch
            {
                return Array.Empty<SingleFlag[]>();
            }
        }

        // v2 form: insert the suffix after the class name -> Namespace.Class(SqlServer, Json).Method. Guarded: if the
        // display name does not start with the class name (e.g. [Fact(DisplayName=...)]), fall back to append so a future
        // custom name changes shape instead of throwing.
        private static void ApplyVariantDisplayName(ITestCase testCase, SingleFlag[] variant)
        {
            if (variant.Length == 0 || testCase is not XunitTestCase xunitTestCase)
            {
                return;
            }

            string suffix = $"({string.Join(", ", variant.Select(v => v.EnumValue))})";
            string name = xunitTestCase.TestCaseDisplayName;
            string className = xunitTestCase.TestMethod.TestClass.TestClassName;

            string patched = name.StartsWith(className, StringComparison.Ordinal) && name.Length >= className.Length
                ? name.Insert(className.Length, suffix)
                : name + " " + suffix;

            DisplayNameField.SetValue(xunitTestCase, patched);
        }

        // Case-level trait merge: only matters for a trait-less case xUnit builds for a malformed test
        // (ExecutionErrorTestCase). Healthy cases already carry the merged method traits.
        private static void ApplyMethodTraits(ITestCase testCase, XunitTestMethod testMethod)
        {
            if (testCase is not XunitTestCase xunitTestCase)
            {
                return;
            }

            foreach (KeyValuePair<string, IReadOnlyCollection<string>> trait in testMethod.Traits)
            {
                if (!xunitTestCase.Traits.TryGetValue(trait.Key, out HashSet<string> values))
                {
                    xunitTestCase.Traits[trait.Key] = values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                foreach (string value in trait.Value)
                {
                    values.Add(value);
                }
            }
        }

        private static SingleFlag[][] Expand(FixtureArgumentSetsAttribute attribute)
        {
            Enum[] sets = attribute.GetArgumentSets();
            Type duplicate = sets.Where(a => a != null).GroupBy(a => a.GetType()).FirstOrDefault(g => g.Count() > 1)?.Key;
            if (duplicate != null)
            {
                throw new InvalidOperationException($"Duplicate fixture dimension of type '{duplicate}'; each dimension needs its own enum type.");
            }

            return sets.Select(e => GetSingleValuedFlags(e).ToArray()).ToArray();
        }

        private static IEnumerable<SingleFlag> GetSingleValuedFlags(Enum value)
        {
            if (value is null)
            {
                yield break;
            }

            long all = Convert.ToInt64(value);
            foreach (Enum flag in Enum.GetValues(value.GetType()))
            {
                long flagValue = Convert.ToInt64(flag);
                if (flagValue != 0 && (flagValue & (flagValue - 1)) == 0 && (all & flagValue) != 0)
                {
                    yield return new SingleFlag(flag);
                }
            }
        }

        private static SingleFlag[][] CartesianProduct(SingleFlag[][] dimensions)
        {
            IEnumerable<IEnumerable<SingleFlag>> product = new[] { Enumerable.Empty<SingleFlag>() };
            foreach (SingleFlag[] dimension in dimensions)
            {
                product = product.SelectMany(accumulator => dimension.Select(x => accumulator.Concat(new[] { x })));
            }

            return product.Select(p => p.ToArray()).ToArray();
        }
    }
}
