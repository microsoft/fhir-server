// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using static Microsoft.Health.Fhir.Tests.Common.Search.SearchValueValidationHelper;
using Task=System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class AddressToStringSearchValueConverterTests : FhirTypedElementToSearchValueConverterTests<AddressToStringSearchValueConverter, Address>
    {
        [Fact]
        public async Task GivenAnAddressWithCity_WhenConverted_ThenAStringSearchValueShouldBeCreated()
        {
            const string city = "Seattle";

            await Test(
                address => address.City = city,
                ValidateString,
                city);
        }

        [Fact]
        public async Task GivenAnAddressWithCountry_WhenConverted_ThenAStringSearchValueShouldBeCreated()
        {
            const string country = "USA";

            await Test(
                address => address.Country = country,
                ValidateString,
                country);
        }

        [Fact]
        public async Task GivenAnAddressWithDistrict_WhenConverted_ThenAStringSearchValueShouldBeCreated()
        {
            const string district = "DC";

            await Test(
                address => address.District = district,
                ValidateString,
                district);
        }

        [Fact]
        public async Task GivenAnAddressWithNoLine_WhenConverted_ThenOneOrMultipleStringSearchValueShouldBeCreated()
        {
            await Test(address => address.Line = null);
        }

        [Theory]
        [InlineData("Line1")]
        [InlineData("Line1", "Line2")]
        public async Task GivenAnAddressWithLine_WhenConverted_ThenOneOrMultipleStringSearchValueShouldBeCreated(params string[] lines)
        {
            await Test(
                address => address.Line = lines,
                ValidateString,
                lines);
        }

        [Fact]
        public async Task GivenAnAddressWithPostalCode_WhenConverted_ThenAStringSearchValueShouldBeCreated()
        {
            const string postalCode = "98052";

            await Test(
                address => address.PostalCode = postalCode,
                ValidateString,
                postalCode);
        }

        [Fact]
        public async Task GivenAnAddressWithState_WhenConverted_ThenAStringSearchValueShouldBeCreated()
        {
            const string state = "Washington";

            await Test(
                address => address.State = state,
                ValidateString,
                state);
        }

        [Fact]
        public async Task GivenAnAddressWithText_WhenConverted_ThenAStringSearchValueShouldBeCreated()
        {
            const string text = "Text";

            await Test(
                address => address.Text = text,
                ValidateString,
                text);
        }
    }
}
