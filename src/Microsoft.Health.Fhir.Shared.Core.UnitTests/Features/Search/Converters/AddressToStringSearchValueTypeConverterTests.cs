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
    public class AddressToStringSearchValueTypeConverterTests : FhirElementToSearchValueTypeConverterTests<AddressToStringSearchValueConverter, Address>
    {
        [Fact]
        public void GivenAnAddressWithCity_WhenConverted_ThenAStringSearchValueShouldBeCreated()
        {
            const string city = "Seattle";

            Test(
                address => address.City = city,
                ValidateString,
                city);
        }

        [Fact]
        public void GivenAnAddressWithCountry_WhenConverted_ThenAStringSearchValueShouldBeCreated()
        {
            const string country = "USA";

            Test(
                address => address.Country = country,
                ValidateString,
                country);
        }

        [Fact]
        public void GivenAnAddressWithDistrict_WhenConverted_ThenAStringSearchValueShouldBeCreated()
        {
            const string district = "DC";

            Test(
                address => address.District = district,
                ValidateString,
                district);
        }

        [Fact]
        public void GivenAnAddressWithNoLine_WhenConverted_ThenOneOrMultipleStringSearchValueShouldBeCreated()
        {
            Test(address => address.Line = null);
        }

        [Theory]
        [InlineData("Line1")]
        [InlineData("Line1", "Line2")]
        public void GivenAnAddressWithLine_WhenConverted_ThenOneOrMultipleStringSearchValueShouldBeCreated(params string[] lines)
        {
            Test(
                address => address.Line = lines,
                ValidateString,
                lines);
        }

        [Fact]
        public void GivenAnAddressWithPostalCode_WhenConverted_ThenAStringSearchValueShouldBeCreated()
        {
            const string postalCode = "98052";

            Test(
                address => address.PostalCode = postalCode,
                ValidateString,
                postalCode);
        }

        [Fact]
        public void GivenAnAddressWithState_WhenConverted_ThenAStringSearchValueShouldBeCreated()
        {
            const string state = "Washington";

            Test(
                address => address.State = state,
                ValidateString,
                state);
        }

        [Fact]
        public void GivenAnAddressWithText_WhenConverted_ThenAStringSearchValueShouldBeCreated()
        {
            const string text = "Text";

            Test(
                address => address.Text = text,
                ValidateString,
                text);
        }
    }
}
