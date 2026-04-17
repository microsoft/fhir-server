// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
        public CustomXunitTestFrameworkDiscoverer(Assembly assembly, IXunitTestCollectionFactory collectionFactory = null)
            : base(new XunitTestAssembly(assembly, configFileName: null, assembly.GetName().Version, UniqueIDGenerator.ForAssembly(assembly.Location, null)), collectionFactory)
        {
        }

        protected override async ValueTask<bool> FindTestsForType(IXunitTestClass testClass, ITestFrameworkDiscoveryOptions discoveryOptions, Func<ITestCase, ValueTask<bool>> callback)
        {
            EnsureArg.IsNotNull(testClass, nameof(testClass));
            EnsureArg.IsNotNull(callback, nameof(callback));
            EnsureArg.IsNotNull(discoveryOptions, nameof(discoveryOptions));

            var attribute = testClass.Class.GetCustomAttributes(typeof(FixtureArgumentSetsAttribute), inherit: false).SingleOrDefault() as FixtureArgumentSetsAttribute;

            if (attribute == null)
            {
                return await base.FindTestsForType(testClass, discoveryOptions, callback);
            }

            // get the class-level parameter sets in the form (Arg1.OptionA, Arg1.OptionB), (Arg2.OptionA, Arg2.OptionB)
            SingleFlag[][] classLevelOpenParameterSets = ExpandEnumFlagsFromAttributeData(attribute);

            // convert these to the form (Arg1.OptionA, Arg2.OptionA), (Arg1.OptionA, Arg2.OptionB), (Arg1.OptionB, Arg2.OptionA), (Arg1.OptionB, Arg2.OptionB)
            SingleFlag[][] classLevelClosedParameterSets = CartesianProduct(classLevelOpenParameterSets).Select(e => e.ToArray()).ToArray();

            // Cache collections (and classes) per variant within a single test class so that all methods of the same class + variant
            // share the same test collection instance. This matches the xunit v2 behavior where methods of a test class belong to
            // a single collection and therefore run serially (and share IClassFixture lifetime). Without this caching, xunit v3
            // schedules each method as its own collection and runs them in parallel, breaking fixture/state sharing assumptions.
            var collectionCache = new Dictionary<string, (FixtureArgumentSetTestCollection Collection, FixtureArgumentSetTestClass Class)>(StringComparer.Ordinal);

            foreach (var method in testClass.Methods)
            {
                var fixtureParameterAttribute = method.GetCustomAttributes(typeof(FixtureArgumentSetsAttribute), inherit: false).SingleOrDefault() as FixtureArgumentSetsAttribute;
                var effectiveAttribute = fixtureParameterAttribute ?? attribute;

                SingleFlag[][] closedSets = classLevelClosedParameterSets;

                if (fixtureParameterAttribute != null)
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

                foreach (SingleFlag[] closedVariant in closedSets)
                {
                    var testClassName = effectiveAttribute.CollectionBehavior == FixtureArgumentSetCollectionBehavior.PerClass
                        ? testClass.Class.FullName
                        : null;

                    // Key the cache by the collection display identity (variant + optional class scoping). Methods that
                    // resolve to the same key share one collection instance.
                    var variantKey = (testClassName ?? string.Empty) + "|" + string.Join(",", closedVariant.Select(v => v.EnumValue));

                    if (!collectionCache.TryGetValue(variantKey, out var cached))
                    {
                        var newCollection = new FixtureArgumentSetTestCollection(testClass.TestCollection.TestAssembly, closedVariant, testClassName);
                        var newClass = new FixtureArgumentSetTestClass(testClass.Class, newCollection, closedVariant, UniqueIDGenerator.ForTestClass(newCollection.UniqueID, testClass.Class.FullName));
                        newClass.ApplyFixtureArguments(closedVariant);
                        cached = (newCollection, newClass);
                        collectionCache[variantKey] = cached;
                    }

                    var closedVariantTestMethod = new FixtureArgumentSetTestMethod(cached.Class, method, closedVariant, uniqueId: UniqueIDGenerator.ForTestMethod(cached.Class.UniqueID, method.Name));

                    closedVariantTestMethod.UpdateArgumentsFromMethod();

                    if (!await FindTestsForMethod(closedVariantTestMethod, discoveryOptions, callback))
                    {
                        return false;
                    }
                }
            }

            return true;
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
