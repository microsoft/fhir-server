// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchValues
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class UriSearchValueTests
    {
        private const string ParamNameUri = "uri";
        private const string ParamNameS = "s";

        private readonly IModelInfoProvider _modelInfoProvider;

        public UriSearchValueTests()
        {
            _modelInfoProvider = MockModelInfoProviderBuilder.Create(FhirSpecification.R4).Build();
        }

        [Fact]
        public void GivenANullUri_WhenInitializing_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameUri, () => new UriSearchValue(null, false));
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        public void GivenAnInvalidUri_WhenInitializing_ThenExceptionShouldBeThrown(string s)
        {
            Assert.Throws<ArgumentException>(ParamNameUri, () => new UriSearchValue(s, false));
        }

        [Fact]
        public void GivenANullString_WhenParsing_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameS, () => UriSearchValue.Parse(null, false, _modelInfoProvider));
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        public void GivenAnInvalidString_WhenParsing_ThenExceptionShouldBeThrown(string s)
        {
            Assert.Throws<ArgumentException>(ParamNameS, () => UriSearchValue.Parse(s, false, _modelInfoProvider));
        }

        [Fact]
        public void GivenAValidString_WhenParsed_ThenCorrectSearchValueShouldBeReturned()
        {
            string expected = "http://uri2";

            UriSearchValue value = UriSearchValue.Parse(expected, false, _modelInfoProvider);

            Assert.NotNull(value);
            Assert.Equal(expected, value.Uri);
        }

        [Fact]
        public void GivenASearchValue_WhenIsValidCompositeComponentIsCalled_ThenTrueShouldBeReturned()
        {
            var value = new UriSearchValue("http://uri", false);

            Assert.True(value.IsValidAsCompositeComponent);
        }

        [Fact]
        public void GivenASearchValue_WhenToStringIsCalled_ThenCorrectStringShouldBeReturned()
        {
            string expected = "http://uri3";

            UriSearchValue value = new UriSearchValue(expected, false);

            Assert.Equal(expected, value.ToString());
        }
    }
}
