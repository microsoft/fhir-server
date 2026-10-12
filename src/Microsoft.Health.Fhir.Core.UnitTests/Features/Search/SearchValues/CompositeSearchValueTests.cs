// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchValues
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class CompositeSearchValueTests
    {
        private const string ParamNameComponents = "components";

        [Fact]
        public void GivenTheSameComponentLists_WhenHashing_ThenHashesAreStableAndEqual()
        {
            // Arrange
            var components = new IReadOnlyList<ISearchValue>[]
            {
                new ISearchValue[] { new StringSearchValue("abc") },
                new ISearchValue[] { new NumberSearchValue(123) },
            };
            var value = new CompositeSearchValue(components);
            var equal = new CompositeSearchValue(new[] { components[0], components[1] });

            // Act
            int hash = value.GetHashCode();

            // Assert
            Assert.True(value.Equals(equal));
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal(hash, value.GetHashCode());
                Assert.Equal(hash, equal.GetHashCode());
            }
        }

        [Fact]
        public void GivenEqualCompositeValues_WhenAddedToHashSet_ThenDuplicatesAreRejectedAndFound()
        {
            // Arrange
            var components = new[] { new ISearchValue[] { new StringSearchValue("abc") } };
            var value = new CompositeSearchValue(components);
            var equal = new CompositeSearchValue(new[] { components[0] });
            var distinctList = new CompositeSearchValue(new[] { new ISearchValue[] { components[0][0] } });
            var set = new HashSet<ISearchValue>();

            // Act / Assert
            Assert.True(set.Add(value));
            Assert.False(set.Add(value));
            Assert.False(set.Add(equal));
            Assert.Contains(equal, set);
            bool foundByHash = set.Contains(value);
            Assert.True(foundByHash);
            Assert.Single(set);
            Assert.False(value.Equals(distinctList));
            Assert.True(set.Add(distinctList));
        }

        [Fact]
        public void GivenANullComponents_WhenInitializing_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameComponents, () => new CompositeSearchValue(null));
        }

        [Fact]
        public void GivenAnEmptyComponents_WhenInitializing_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentException>(ParamNameComponents, () => new CompositeSearchValue(new IReadOnlyList<ISearchValue>[] { }));
        }

        [Fact]
        public void GivenASearchValue_WhenIsValidCompositeComponentIsCalled_ThenFalseShouldBeReturned()
        {
            var components = new ISearchValue[]
            {
                new StringSearchValue("abc"),
            };

            var value = new CompositeSearchValue(new[] { components });

            Assert.False(value.IsValidAsCompositeComponent);
        }

        [Fact]
        public void GivenASearchValue_WhenToStringIsCalled_ThenCorrectStringShouldBeReturned()
        {
            var components = new ISearchValue[][]
            {
                new ISearchValue[]
                {
                    new TokenSearchValue("system1", "code1", "text1"),
                    new TokenSearchValue("system2", "code2", "text2"),
                },
                new ISearchValue[]
                {
                    new NumberSearchValue(123),
                    new NumberSearchValue(789),
                },
            };

            var value = new CompositeSearchValue(components);

            Assert.Equal("(system1|code1), (system2|code2) $ (123), (789)", value.ToString());
        }
    }
}
