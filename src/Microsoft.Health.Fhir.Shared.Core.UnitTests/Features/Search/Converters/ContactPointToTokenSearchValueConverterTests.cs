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
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ContactPointToTokenSearchValueConverterTests : FhirTypedElementToSearchValueConverterTests<ContactPointToTokenSearchValueConverter, ContactPoint>
    {
        [Fact]
        public async Task GivenAContactPointWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            await Test(cp => cp.Value = null);
        }

        [Fact]
        public async Task GivenAContactPointWithValue_WhenConverted_ThenATokenSearchValueShouldBeCreated()
        {
            const string number = "123";

            await Test(
                cp =>
                {
                    cp.Use = ContactPoint.ContactPointUse.Home;
                    cp.Value = number;
                },
                ValidateToken,
                new Token("home", number));
        }
    }
}
