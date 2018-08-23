// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Legacy.SearchValues
{
    public class SelectorExtensionsTests
    {
        [Fact]
        public void GivenANullCollection_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            Func<TestResource, IEnumerable<object>> collectionSelector = resource => null;

            IEnumerable<object> results = collectionSelector.ExtractNonEmptyCollection(new TestResource());

            Assert.NotNull(results);
            Assert.Empty(results);
        }

        [Fact]
        public void GivenCollectionWithNullEntry_WhenExtracting_ThenNullEntryShouldBeFiltered()
        {
            Func<TestResource, IEnumerable<object>> collectionSelector = resource => new object[] { 1, null, 2 };

            IEnumerable<object> results = collectionSelector.ExtractNonEmptyCollection(new TestResource());

            Assert.NotNull(results);
            Assert.Equal(new object[] { 1, 2 }, results);
        }

        [Fact]
        public void GivenANullCompositeToken_WhenExtracting_ThenEmptyResultShouldBeReturned()
        {
            Func<TestResource, CodeableConcept> compositeTokenSelector = resource => null;

            IEnumerable<Coding> results = compositeTokenSelector.ExtractNonEmptyCoding(new TestResource());

            Assert.NotNull(results);
            Assert.Empty(results);
        }

        [Fact]
        public void GivenACodeWithNullCoding_WhenExtracting_ThenEmptyResultShouldBeRetruend()
        {
            var codeableConcept = new CodeableConcept();

            codeableConcept.Coding = null;

            Func<TestResource, CodeableConcept> compositeTokenSelector = resource => codeableConcept;

            IEnumerable<Coding> results = compositeTokenSelector.ExtractNonEmptyCoding(new TestResource());

            Assert.NotNull(results);
            Assert.Empty(results);
        }

        [Fact]
        public void GivenACodeWithNullCodingEntry_WhenExtracting_ThenNullEntryShouldBeFiltered()
        {
            string system = "system";
            string code = "code";

            var codeableConcept = new CodeableConcept();

            codeableConcept.Coding.AddRange(new[]
            {
                new Coding(system, code),
                new Coding(string.Empty, string.Empty),
                null,
                new Coding(system, string.Empty),
                new Coding(string.Empty, code),
            });

            Func<TestResource, CodeableConcept> compositeTokenSelector = resource => codeableConcept;

            IEnumerable<Coding> results = compositeTokenSelector.ExtractNonEmptyCoding(new TestResource());

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidateCoding(system, code, r),
                r => ValidateCoding(system, string.Empty, r),
                r => ValidateCoding(string.Empty, code, r));
        }

        [Fact]
        public void GivenANullCollection_WhenExtractingManyCodings_ThenEmptyResultShouldBeReturned()
        {
            Func<IEnumerable<object>, IEnumerable<CodeableConcept>> collectionSelector = resource => null;

            IEnumerable<object> results = collectionSelector.ExtractNonEmptyCoding(null);

            Assert.NotNull(results);
            Assert.Empty(results);
        }

        [Fact]
        public void GivenAnEmptyCollection_WhenExtractingManyCodings_ThenEmptyResultShouldBeReturned()
        {
            var collection = new List<Condition.EvidenceComponent>();
            Func<IEnumerable<Condition.EvidenceComponent>, IEnumerable<CodeableConcept>> collectionSelector = resource => collection.SelectMany(item => item.Code);

            IEnumerable<object> results = collectionSelector.ExtractNonEmptyCoding(collection);

            Assert.NotNull(results);
            Assert.Empty(results);
        }

        [Fact]
        public void GivenACollectionWithEmptyCodes_WhenExtractingManyCodings_ThenEmptyResultShouldBeReturned()
        {
            var collection = new List<Condition.EvidenceComponent>();
            collection.Add(new Condition.EvidenceComponent());
            Func<IEnumerable<Condition.EvidenceComponent>, IEnumerable<CodeableConcept>> collectionSelector = resource => collection.SelectMany(item => item.Code);

            IEnumerable<object> results = collectionSelector.ExtractNonEmptyCoding(collection);

            Assert.NotNull(results);
            Assert.Empty(results);
        }

        [Fact]
        public void GivenACollectionWithOnlyDetail_WhenExtractingManyCodings_ThenEmptyResultShouldBeReturned()
        {
            var collection = new List<Condition.EvidenceComponent>
            {
                new Condition.EvidenceComponent
                {
                    Detail = new List<ResourceReference>
                    {
                        new ResourceReference("Patient/1"),
                    },
                },
            };

            Func<IEnumerable<Condition.EvidenceComponent>, IEnumerable<CodeableConcept>> collectionSelector = resource => collection.SelectMany(item => item.Code);

            IEnumerable<object> results = collectionSelector.ExtractNonEmptyCoding(collection);

            Assert.NotNull(results);
            Assert.Empty(results);
        }

        [Fact]
        public void GivenACollectionWithMultipleDetail_WhenExtractingManyCodings_ThenEmptyResultShouldBeReturned()
        {
            var collection = new List<Condition.EvidenceComponent>
            {
                new Condition.EvidenceComponent
                {
                    Detail = new List<ResourceReference>
                    {
                        new ResourceReference("Patient/1"),
                    },
                },
                new Condition.EvidenceComponent
                {
                    Detail = new List<ResourceReference>
                    {
                        new ResourceReference("Patient/1"),
                    },
                },
            };

            Func<IEnumerable<Condition.EvidenceComponent>, IEnumerable<CodeableConcept>> collectionSelector = resource => collection.SelectMany(item => item.Code);

            IEnumerable<object> results = collectionSelector.ExtractNonEmptyCoding(collection);

            Assert.NotNull(results);
            Assert.Empty(results);
        }

        [Fact]
        public void GivenACollectionWithSingleCoding_WhenExtractingManyCodings_ThenResultsShouldBeReturned()
        {
            var system = "system";
            var code = "code";

            var collection = new List<Condition.EvidenceComponent>
            {
                new Condition.EvidenceComponent
                {
                    Code = new List<CodeableConcept>
                    {
                        new CodeableConcept(system, code),
                    },
                },
            };

            Func<IEnumerable<Condition.EvidenceComponent>, IEnumerable<CodeableConcept>> collectionSelector = resource => collection.SelectMany(item => item.Code);

            IEnumerable<Coding> results = collectionSelector.ExtractNonEmptyCoding(collection);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidateCoding(system, code, r));
        }

        [Fact]
        public void GivenAMultiCollectionWithSingleCoding_WhenExtractingManyCodings_ThenResultsShouldBeReturned()
        {
            var system = "system";
            var code = "code";

            var collection = new List<Condition.EvidenceComponent>
            {
                new Condition.EvidenceComponent
                {
                    Code = new List<CodeableConcept>
                    {
                        new CodeableConcept(system, code),
                    },
                },
                new Condition.EvidenceComponent
                {
                    Code = new List<CodeableConcept>
                    {
                        new CodeableConcept(system, code),
                    },
                },
            };

            Func<IEnumerable<Condition.EvidenceComponent>, IEnumerable<CodeableConcept>> collectionSelector = resource => collection.SelectMany(item => item.Code);

            IEnumerable<Coding> results = collectionSelector.ExtractNonEmptyCoding(collection);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidateCoding(system, code, r),
                r => ValidateCoding(system, code, r));
        }

        [Fact]
        public void GivenAMultiCollectionWithMultipleCodings_WhenExtractingManyCodings_ThenResultsShouldBeReturnedWithEmptyFiltered()
        {
            var system = "system";
            var code = "code";

            var collection = new List<Condition.EvidenceComponent>
            {
                new Condition.EvidenceComponent
                {
                    Code = new List<CodeableConcept>
                    {
                        new CodeableConcept(system, code),
                        new CodeableConcept(string.Empty, code),
                        new CodeableConcept(string.Empty, string.Empty),
                    },
                },
                new Condition.EvidenceComponent
                {
                    Code = new List<CodeableConcept>
                    {
                        new CodeableConcept(system, code),
                        new CodeableConcept(system, string.Empty),
                    },
                },
            };

            Func<IEnumerable<Condition.EvidenceComponent>, IEnumerable<CodeableConcept>> collectionSelector = resource => collection.SelectMany(item => item.Code);

            IEnumerable<Coding> results = collectionSelector.ExtractNonEmptyCoding(collection);

            Assert.NotNull(results);
            Assert.Collection(
                results,
                r => ValidateCoding(system, code, r),
                r => ValidateCoding(string.Empty, code, r),
                r => ValidateCoding(system, code, r),
                r => ValidateCoding(system, string.Empty, r));
        }

        private void ValidateCoding(string expectedSystem, string expectedCode, Coding actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expectedSystem, actual.System);
            Assert.Equal(expectedCode, actual.Code);
        }
    }
}
