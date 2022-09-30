// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
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
    public class CodeableConceptToTokenSearchValueConverterTests : FhirTypedElementToSearchValueConverterTests<CodeableConceptToTokenSearchValueConverter, CodeableConcept>
    {
        public static IEnumerable<object[]> GetMultipleCodingDataSource()
        {
            yield return new[] { new Token("system", "code") };
            yield return new[] { new Token("system", "code", "text") };
            yield return new[] { new Token("system1", "code1"), new Token("system2", "code2", "text2") };
        }

        [Fact]
        public async Task GivenACodeableConceptWithText_WhenConverted_ThenATokenSearchValueShouldBeCreated()
        {
            const string text = "text";

            await Test(
                cc => cc.Text = text,
                ValidateToken,
                new Token(text: text));
        }

        [Fact]
        public async Task GivenACodeableConceptWithTextThatIsTheSameAsTheDisplayOfACoding_WhenConverted_ThenATokenSearchValueShouldNotBeCreatedForTheConceptText()
        {
            const string system = "system";
            const string text = "text";

            await Test(
                cc =>
                {
                    cc.Text = text;
                    cc.Coding.Add(new Coding(system, null, text));
                },
                ValidateToken,
                new Token(system: system, text: text));
        }

        [Fact]
        public async Task GivenACodeableConceptWithTextThatIsDifferentThanTheDisplayOfACoding_WhenConverted_ThenATokenSearchValueShouldBeCreatedForTheConceptText()
        {
            const string system = "system";
            const string conceptText = "conceptText";
            const string codingDisplayText = "codingDisplay";

            await Test(
                cc =>
                {
                    cc.Text = conceptText;
                    cc.Coding.Add(new Coding(system, null, codingDisplayText));
                },
                ValidateToken,
                new Token(system: system, text: codingDisplayText),
                new Token(text: conceptText));
        }

        [Fact]
        public async Task GivenACodeableConceptWithNullCoding_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            await Test(cc => cc.Coding = null);
        }

        [Theory]
        [MemberData(nameof(GetMultipleCodingDataSource))]
        public async Task GivenACodeableConceptWithCodings_WhenConverted_ThenOneOrMultipleTokenSearchValuesShouldBeCreated(params Token[] tokens)
        {
            await Test(
                cc => cc.Coding.AddRange(tokens.Select(token => new Coding(token.System, token.Code, token.Text))),
                ValidateToken,
                tokens);
        }

        [Fact]
        public async Task GivenACodeableConceptWithEmptyCoding_WhenConverted_ThenEmptyCodingShouldBeExcluded()
        {
            const string system = "system";
            const string text = "text";

            await Test(
                cc =>
                {
                    cc.Coding.Add(new Coding(null, null, null));
                    cc.Coding.Add(null);
                    cc.Coding.Add(new Coding(system, null, text));
                },
                ValidateToken,
                new Token(system, null, text));
        }

        [Theory]
        [InlineData("system", null, null)]
        [InlineData(null, "code", null)]
        [InlineData(null, null, "text")]
        public async Task GivenACodeableConceptWithPartialCoding_WhenConverted_ThenATokenSearchValueShouldBeCreated(string system, string code, string text)
        {
            await Test(
                cc =>
                {
                    cc.Coding.Add(new Coding(system, code, text));
                },
                ValidateToken,
                new Token(system, code, text));
        }
    }
}
