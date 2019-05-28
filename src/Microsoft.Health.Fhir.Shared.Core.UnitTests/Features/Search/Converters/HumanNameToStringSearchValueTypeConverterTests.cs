// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Xunit;
using static Microsoft.Health.Fhir.Tests.Common.Search.SearchValueValidationHelper;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters
{
    public class HumanNameToStringSearchValueTypeConverterTests : FhirElementToSearchValueTypeConverterTests<HumanNameToStringSearchValueTypeConverter, HumanName>
    {
        [Fact]
        public void GivenAHumaneNameWithNoGiven_WhenConverted_ThenOneOrMoreStringSearchValuesShouldBeCreated()
        {
            Test(hn => hn.Given = null);
        }

        [Theory]
        [InlineData("given")]
        [InlineData("given1", "given2")]
        public void GivenAHumaneNameWithGiven_WhenConverted_ThenOneOrMoreStringSearchValuesShouldBeCreated(params string[] given)
        {
            Test(
                hn => hn.Given = given,
                ValidateString,
                given);
        }

        [Fact]
        public void GivenAnHumanNameWithFamily_WhenConverted_ThenAStringSearchValueShouldBeCreated()
        {
            const string family = "Doe";

            Test(
                hn => hn.Family = family,
                ValidateString,
                family);
        }

        [Fact]
        public void GivenAnHumanNameWithNoPrefix_WhenConverted_ThenOneOrMoreStringSearchValueShouldBeCreated()
        {
            Test(hn => hn.Prefix = null);
        }

        [Theory]
        [InlineData("prefix")]
        [InlineData("prefix1", "prefix2")]
        public void GivenAnHumanNameWithPrefix_WhenConverted_ThenOneOrMoreStringSearchValueShouldBeCreated(params string[] prefix)
        {
            Test(
                hn => hn.Prefix = prefix,
                ValidateString,
                prefix);
        }

        [Fact]
        public void GivenAnHumanNameWithNoSuffix_WhenConverted_ThenAStringSearchValueShouldBeCreated()
        {
            Test(hn => hn.Suffix = null);
        }

        [Theory]
        [InlineData("suffix")]
        [InlineData("suffix1", "suffix2")]
        public void GivenAnHumanNameWithSuffix_WhenConverted_ThenAStringSearchValueShouldBeCreated(params string[] suffix)
        {
            Test(
                hn => hn.Suffix = suffix,
                ValidateString,
                suffix);
        }

        [Fact]
        public void GivenAnHumanNameWithText_WhenConverted_ThenAStringSearchValueShouldBeCreated()
        {
            const string text = "text";

            Test(
                hn => hn.Text = text,
                ValidateString,
                text);
        }
    }
}
