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
            var classAttribute = testClass.Class.GetCustomAttributes(typeof(FixtureArgumentSetsAttribute), false).SingleOrDefault() as FixtureArgumentSetsAttribute;
            if (classAttribute == null && !testClass.Methods.Any(m => m.IsDefined(typeof(FixtureArgumentSetsAttribute), false)))
            {
                return await base.FindTestsForType(testClass, options, callback);
            }

            SingleFlag[][] classOpenSets = classAttribute == null ? Array.Empty<SingleFlag[]>() : Expand(classAttribute);
            SingleFlag[][] classClosedSets = classOpenSets.Length == 0 ? Array.Empty<SingleFlag[]>() : CartesianProduct(classOpenSets);

            foreach (MethodInfo method in testClass.Methods)
            {
                var methodAttribute = method.GetCustomAttributes(typeof(FixtureArgumentSetsAttribute), false).SingleOrDefault() as FixtureArgumentSetsAttribute;
                if (!await ExpandMethod(testClass, method, classAttribute, methodAttribute, classOpenSets, classClosedSets, options, callback))
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
