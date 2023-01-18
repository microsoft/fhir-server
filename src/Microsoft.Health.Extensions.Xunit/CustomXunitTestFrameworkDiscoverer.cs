// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// An implementation of <see cref="XunitTestFrameworkDiscoverer"/> that supports discovering tests with parameterized fixtures.
    /// </summary>
    internal sealed class CustomXunitTestFrameworkDiscoverer : XunitTestFrameworkDiscoverer, ITestFrameworkDiscoverer
    {
        public CustomXunitTestFrameworkDiscoverer(IAssemblyInfo assemblyInfo, ISourceInformationProvider sourceProvider, IMessageSink diagnosticMessageSink, IXunitTestCollectionFactory collectionFactory = null)
            : base(assemblyInfo, sourceProvider, diagnosticMessageSink, collectionFactory)
        {
        }

        protected override bool FindTestsForType(ITestClass testClass, bool includeSourceInformation, IMessageBus messageBus, ITestFrameworkDiscoveryOptions discoveryOptions)
        {
            EnsureArg.IsNotNull(testClass, nameof(testClass));
            EnsureArg.IsNotNull(messageBus, nameof(messageBus));
            EnsureArg.IsNotNull(discoveryOptions, nameof(discoveryOptions));

            var attributeInfo = testClass.Class.GetCustomAttributes(typeof(FixtureArgumentSetsAttribute)).SingleOrDefault();

            if (attributeInfo == null)
            {
                return base.FindTestsForType(testClass, includeSourceInformation, messageBus, discoveryOptions);
            }

            // get the class-level parameter sets in the form (Arg1.OptionA, Arg1.OptionB), (Arg2.OptionA, Arg2.OptionB)
            SingleFlag[][] classLevelOpenParameterSets = ExpandEnumFlagsFromAttributeData(attributeInfo);

            // convert these to the form (Arg1.OptionA, Arg2.OptionA), (Arg1.OptionA, Arg2.OptionB), (Arg1.OptionB, Arg2.OptionA), (Arg1.OptionB, Arg2.OptionB)
            SingleFlag[][] classLevelClosedParameterSets = CartesianProduct(classLevelOpenParameterSets).Select(e => e.ToArray()).ToArray();

            foreach (var method in testClass.Class.GetMethods(true))
            {
                IAttributeInfo fixtureParameterAttributeInfo = method.GetCustomAttributes(typeof(FixtureArgumentSetsAttribute)).SingleOrDefault();

                SingleFlag[][] closedSets = classLevelClosedParameterSets;

                if (fixtureParameterAttributeInfo != null)
                {
                    // get the method-level parameter sets in the form (Arg1.OptionA, Arg1.OptionB), (Arg2.OptionA, Arg2.OptionB)
                    SingleFlag[][] methodLevelOpenParameterSets = ExpandEnumFlagsFromAttributeData(fixtureParameterAttributeInfo);

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
                    var closedVariantTestClass = new TestClassWithFixtureArguments(testClass.TestCollection, testClass.Class, closedVariant);
                    var closedVariantTestMethod = new TestMethod(closedVariantTestClass, method);

                    if (!FindTestsForMethod(closedVariantTestMethod, includeSourceInformation, messageBus, discoveryOptions))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static SingleFlag[][] ExpandEnumFlagsFromAttributeData(IAttributeInfo attributeInfo)
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

            return attributeInfo
                .GetConstructorArguments()
                .Cast<Enum>()
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
