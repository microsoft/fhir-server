// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Extensions.Xunit;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using static Microsoft.Health.Fhir.Tests.Common.Search.SearchValueValidationHelper;
using Task=System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class HumanNameToStringSearchValueConverterTests : FhirTypedElementToSearchValueConverterTests<HumanNameToStringSearchValueConverter, HumanName>
    {
        [RetryFact]
        public async Task GivenAHumaneNameWithNoGiven_WhenConverted_ThenOneOrMoreStringSearchValuesShouldBeCreated()
        {
            await Test(hn => hn.Given = null);
        }

        [RetryTheory]
        [InlineData("given")]
        [InlineData("given1", "given2")]
        public async Task GivenAHumaneNameWithGiven_WhenConverted_ThenOneOrMoreStringSearchValuesShouldBeCreated(params string[] given)
        {
            await Test(
                hn => hn.Given = given,
                ValidateString,
                given);
        }

        [RetryFact]
        public async Task GivenAnHumanNameWithFamily_WhenConverted_ThenAStringSearchValueShouldBeCreated()
        {
            const string family = "Doe";

            await Test(
                hn => hn.Family = family,
                ValidateString,
                family);
        }

        [RetryFact]
        public async Task GivenAnHumanNameWithNoPrefix_WhenConverted_ThenOneOrMoreStringSearchValueShouldBeCreated()
        {
            await Test(hn => hn.Prefix = null);
        }

        [RetryTheory]
        [InlineData("prefix")]
        [InlineData("prefix1", "prefix2")]
        public async Task GivenAnHumanNameWithPrefix_WhenConverted_ThenOneOrMoreStringSearchValueShouldBeCreated(params string[] prefix)
        {
            await Test(
                hn => hn.Prefix = prefix,
                ValidateString,
                prefix);
        }

        [RetryFact]
        public async Task GivenAnHumanNameWithNoSuffix_WhenConverted_ThenAStringSearchValueShouldBeCreated()
        {
            await Test(hn => hn.Suffix = null);
        }

        [RetryTheory]
        [InlineData("suffix")]
        [InlineData("suffix1", "suffix2")]
        public async Task GivenAnHumanNameWithSuffix_WhenConverted_ThenAStringSearchValueShouldBeCreated(params string[] suffix)
        {
            await Test(
                hn => hn.Suffix = suffix,
                ValidateString,
                suffix);
        }

        [RetryFact]
        public async Task GivenAnHumanNameWithText_WhenConverted_ThenAStringSearchValueShouldBeCreated()
        {
            const string text = "text";

            await Test(
                hn => hn.Text = text,
                ValidateString,
                text);
        }
    }
}
