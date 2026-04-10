// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Conformance)]
    public class ResourceKeyTests
    {
        public ResourceKeyTests()
        {
            ModelInfoProvider.SetProvider(MockModelInfoProviderBuilder.Create(FhirSpecification.R4).AddKnownTypes(KnownResourceTypes.Group).Build());
        }

        [Fact]
        public void GivenTwoResourceKeys_WhenResourceTypesAreDifferent_ThenCompareByResourceType()
        {
            var key1 = new ResourceKey("Patient", "123");
            var key2 = new ResourceKey("Observation", "123");

            // Patient comes after Observation alphabetically
            Assert.True(key1.CompareTo(key2) > 0);
            Assert.True(key2.CompareTo(key1) < 0);
        }

        [Fact]
        public void GivenTwoResourceKeys_WhenResourceTypesAreSameAndIdsAreDifferent_ThenCompareById()
        {
            var key1 = new ResourceKey("Patient", "123");
            var key2 = new ResourceKey("Patient", "456");

            // "123" comes before "456" alphabetically
            Assert.True(key1.CompareTo(key2) < 0);
            Assert.True(key2.CompareTo(key1) > 0);
        }

        [Fact]
        public void GivenTwoResourceKeys_WhenResourceTypeAndIdAreSameButVersionsAreDifferent_ThenCompareByVersion()
        {
            var key1 = new ResourceKey("Patient", "123", "1");
            var key2 = new ResourceKey("Patient", "123", "2");

            // Numeric versions: 1 < 2
            Assert.True(key1.CompareTo(key2) < 0);
            Assert.True(key2.CompareTo(key1) > 0);
        }

        [Fact]
        public void GivenTwoResourceKeys_WhenVersionsAreNumeric_ThenCompareNumerically()
        {
            var key1 = new ResourceKey("Patient", "123", "2");
            var key2 = new ResourceKey("Patient", "123", "10");

            // Numeric comparison: 2 < 10 (not string comparison where "10" < "2")
            Assert.True(key1.CompareTo(key2) < 0);
            Assert.True(key2.CompareTo(key1) > 0);
        }

        [Fact]
        public void GivenTwoResourceKeys_WhenVersionsAreNotNumeric_ThenCompareAsStrings()
        {
            var key1 = new ResourceKey("Patient", "123", "v1");
            var key2 = new ResourceKey("Patient", "123", "v2");

            // String comparison
            Assert.True(key1.CompareTo(key2) < 0);
            Assert.True(key2.CompareTo(key1) > 0);
        }

        [Fact]
        public void GivenTwoResourceKeys_WhenOneHasVersionAndOtherDoesNot_ThenAreEqual()
        {
            var key1 = new ResourceKey("Patient", "123", "1");
            var key2 = new ResourceKey("Patient", "123");

            // When one version is null, they should be equal
            Assert.Equal(0, key1.CompareTo(key2));
            Assert.Equal(0, key2.CompareTo(key1));
        }

        [Fact]
        public void GivenTwoResourceKeys_WhenBothHaveNoVersion_ThenAreEqual()
        {
            var key1 = new ResourceKey("Patient", "123");
            var key2 = new ResourceKey("Patient", "123");

            Assert.Equal(0, key1.CompareTo(key2));
        }

        [Fact]
        public void GivenTwoIdenticalResourceKeys_WhenCompared_ThenAreEqual()
        {
            var key1 = new ResourceKey("Patient", "123", "1");
            var key2 = new ResourceKey("Patient", "123", "1");

            Assert.Equal(0, key1.CompareTo(key2));
        }

        [Fact]
        public void GivenTwoResourceKeys_WhenUsingLessThanOperator_ThenReturnsCorrectResult()
        {
            var key1 = new ResourceKey("Patient", "123", "1");
            var key2 = new ResourceKey("Patient", "123", "2");

            Assert.True(key1 < key2);
            Assert.False(key2 < key1);
        }

        [Fact]
        public void GivenTwoResourceKeys_WhenUsingLessThanOrEqualOperator_ThenReturnsCorrectResult()
        {
            var key1 = new ResourceKey("Patient", "123", "1");
            var key2 = new ResourceKey("Patient", "123", "2");
            var key3 = new ResourceKey("Patient", "123", "1");

            Assert.True(key1 <= key2);
            Assert.True(key1 <= key3);
            Assert.False(key2 <= key1);
        }

        [Fact]
        public void GivenTwoResourceKeys_WhenUsingGreaterThanOperator_ThenReturnsCorrectResult()
        {
            var key1 = new ResourceKey("Patient", "123", "1");
            var key2 = new ResourceKey("Patient", "123", "2");

            Assert.True(key2 > key1);
            Assert.False(key1 > key2);
        }

        [Fact]
        public void GivenTwoResourceKeys_WhenUsingGreaterThanOrEqualOperator_ThenReturnsCorrectResult()
        {
            var key1 = new ResourceKey("Patient", "123", "1");
            var key2 = new ResourceKey("Patient", "123", "2");
            var key3 = new ResourceKey("Patient", "123", "1");

            Assert.True(key2 >= key1);
            Assert.True(key1 >= key3);
            Assert.False(key1 >= key2);
        }

        [Fact]
        public void GivenNullResourceKey_WhenUsingLessThanOperator_ThenReturnsCorrectResult()
        {
            ResourceKey nullKey = null;
            var key = new ResourceKey("Patient", "123");

            Assert.True(nullKey < key);
            Assert.False(key < nullKey);
        }

        [Fact]
        public void GivenNullResourceKey_WhenUsingLessThanOrEqualOperator_ThenReturnsCorrectResult()
        {
            ResourceKey nullKey = null;
            var key = new ResourceKey("Patient", "123");

            Assert.True(nullKey <= key);
            Assert.False(key <= nullKey);
        }

        [Fact]
        public void GivenNullResourceKey_WhenUsingGreaterThanOperator_ThenReturnsCorrectResult()
        {
            ResourceKey nullKey = null;
            var key = new ResourceKey("Patient", "123");

            Assert.True(key > nullKey);
            Assert.False(nullKey > key);
        }

        [Fact]
        public void GivenNullResourceKey_WhenUsingGreaterThanOrEqualOperator_ThenReturnsCorrectResult()
        {
            ResourceKey nullKey = null;
            var key = new ResourceKey("Patient", "123");

            Assert.True(key >= nullKey);
            Assert.False(nullKey >= key);
        }

        [Fact]
        public void GivenResourceKeys_WhenComparisonIsCaseSensitive_ThenCompareCorrectly()
        {
            var key1 = new ResourceKey("Patient", "abc");
            var key2 = new ResourceKey("Patient", "ABC");

            // Case-sensitive comparison should treat them as different
            Assert.NotEqual(0, key1.CompareTo(key2));
        }

        [Fact]
        public void GivenResourceKeys_WhenSortingList_ThenSortsCorrectly()
        {
            var keys = new List<ResourceKey>
            {
                new ResourceKey("Patient", "123", "3"),
                new ResourceKey("Observation", "456", "1"),
                new ResourceKey("Patient", "123", "1"),
                new ResourceKey("Patient", "456", "1"),
                new ResourceKey("Patient", "123", "2"),
            };

            keys.Sort();

            // Expected order: Observation first, then Patient sorted by Id, then by version
            Assert.Equal("Observation", keys[0].ResourceType);
            Assert.Equal("Patient", keys[1].ResourceType);
            Assert.Equal("123", keys[1].Id);
            Assert.Equal("1", keys[1].VersionId);
            Assert.Equal("Patient", keys[2].ResourceType);
            Assert.Equal("123", keys[2].Id);
            Assert.Equal("2", keys[2].VersionId);
            Assert.Equal("Patient", keys[3].ResourceType);
            Assert.Equal("123", keys[3].Id);
            Assert.Equal("3", keys[3].VersionId);
            Assert.Equal("Patient", keys[4].ResourceType);
            Assert.Equal("456", keys[4].Id);
        }
    }
}
