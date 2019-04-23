// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Microsoft.Health.Extensions.Xunit
{
    public class FixtureArgumentSetsXunitTestFrameworkDiscoverer : XunitTestFrameworkDiscoverer, ITestFrameworkDiscoverer
    {
        public FixtureArgumentSetsXunitTestFrameworkDiscoverer(IAssemblyInfo assemblyInfo, ISourceInformationProvider sourceProvider, IMessageSink diagnosticMessageSink, IXunitTestCollectionFactory collectionFactory = null)
            : base(assemblyInfo, sourceProvider, diagnosticMessageSink, collectionFactory)
        {
        }

        protected override ITestClass CreateTestClass(ITypeInfo @class)
        {
            var originalTestClass = base.CreateTestClass(@class);
            var attributeInfo = originalTestClass.Class.GetCustomAttributes(typeof(FixtureArgumentSetsAttribute)).SingleOrDefault();

            if (attributeInfo == null)
            {
                return originalTestClass;
            }

            return new FixtureArgumentSetsTestClass(originalTestClass, EnumHelper.ExpandEnumFlagsFromAttributeData(attributeInfo));
        }

        protected override bool FindTestsForType(ITestClass testClass, bool includeSourceInformation, IMessageBus messageBus, ITestFrameworkDiscoveryOptions discoveryOptions)
        {
            if (!(testClass is FixtureArgumentSetsTestClass fixtureArgumentsSetsTestClass))
            {
                return base.FindTestsForType(testClass, includeSourceInformation, messageBus, discoveryOptions);
            }

            var classLevelClosedSets = CartesianProduct(fixtureArgumentsSetsTestClass.ParameterSets).Select(e => e.ToArray()).ToArray();

            foreach (var method in testClass.Class.GetMethods(true))
            {
                IAttributeInfo fixtureParameterAttributeInfo = method.GetCustomAttributes(typeof(FixtureArgumentSetsAttribute)).SingleOrDefault();

                SingleFlagEnum[][] closedSets = classLevelClosedSets;

                if (fixtureParameterAttributeInfo != null)
                {
                    SingleFlagEnum[][] methodLevelOpenSets = EnumHelper.ExpandEnumFlagsFromAttributeData(fixtureParameterAttributeInfo);
                    Array.Resize(ref methodLevelOpenSets, fixtureArgumentsSetsTestClass.ParameterSets.Length);

                    bool hasOverride = false;
                    for (int i = 0; i < methodLevelOpenSets.Length; i++)
                    {
                        if (methodLevelOpenSets[i]?.Length > 0)
                        {
                            hasOverride = true;
                        }
                        else
                        {
                            methodLevelOpenSets[i] = fixtureArgumentsSetsTestClass.ParameterSets[i];
                        }
                    }

                    if (hasOverride)
                    {
                        closedSets = CartesianProduct(methodLevelOpenSets).Select(e => e.ToArray()).ToArray();
                    }
                }

                foreach (SingleFlagEnum[] closedVariant in closedSets)
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

        /// <summary>
        /// Creates the cartesian product.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements of the input sequences.</typeparam>
        /// <param name="sequences">The input sequence.</param>
        /// <returns>An <see cref="IEnumerable{T}"/> that contains the cartesian product of the input sequences.</returns>
        public static IEnumerable<IEnumerable<TSource>> CartesianProduct<TSource>(IEnumerable<IEnumerable<TSource>> sequences)
        {
            IEnumerable<IEnumerable<TSource>> emptyProduct = new[] { Enumerable.Empty<TSource>() };

            return sequences.Aggregate(
                emptyProduct,
                (accumulator, sequence) => accumulator.SelectMany(a => sequence.Select(s => a.Concat(Enumerable.Repeat(s, 1)))));
        }

        private class FixtureArgumentSetsTestClass : ITestClass
        {
            private readonly ITestClass _testClassImplementation;

            [Obsolete("Intended to be called by the deserializer only.")]
            public FixtureArgumentSetsTestClass() => throw new NotSupportedException();

            public FixtureArgumentSetsTestClass(ITestClass testClassImplementation, SingleFlagEnum[][] parameterSets)
            {
                _testClassImplementation = testClassImplementation;
                ParameterSets = parameterSets;
            }

            public SingleFlagEnum[][] ParameterSets { get; }

            public ITypeInfo Class => _testClassImplementation.Class;

            public ITestCollection TestCollection => _testClassImplementation.TestCollection;

            void IXunitSerializable.Deserialize(IXunitSerializationInfo info) => throw new NotSupportedException();

            void IXunitSerializable.Serialize(IXunitSerializationInfo info) => throw new NotSupportedException();
        }
    }
}
