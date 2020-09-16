// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Xunit;
using static Microsoft.Health.Fhir.Tests.Common.Search.SearchValueValidationHelper;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters
{
    public class CodeableConceptToTokenSearchValueTypeConverterTests : FhirElementToSearchValueTypeConverterTests<CodeableConceptToTokenSearchValueTypeConverter, CodeableConcept>
    {
        public static IEnumerable<object[]> GetMultipleCodingDataSource()
        {
            yield return new[] { new Token("system", "code") };
            yield return new[] { new Token("system", "code", "text") };
            yield return new[] { new Token("system1", "code1"), new Token("system2", "code2", "text2") };
        }

        [Fact]
        public void GivenACodeableConceptWithText_WhenConverted_ThenATokenSearchValueShouldBeCreated()
        {
            const string text = "text";

            Test(
                cc => cc.Text = text,
                ValidateToken,
                new Token(text: text));
        }

        [Fact]
        public void GivenACodeableConceptWithTextThatIsTheSameAsTheDisplayOfACoding_WhenConverted_ThenATokenSearchValueShouldNotBeCreatedForTheConceptText()
        {
            const string system = "system";
            const string text = "text";

            Test(
                cc =>
                {
                    cc.Text = text;
                    cc.Coding.Add(new Coding(system, null, text));
                },
                ValidateToken,
                new Token(system: system, text: text));
        }

        [Fact]
        public void GivenACodeableConceptWithTextThatIsDifferentThanTheDisplayOfACoding_WhenConverted_ThenATokenSearchValueShouldBeCreatedForTheConceptText()
        {
            const string system = "system";
            const string conceptText = "conceptText";
            const string codingDisplayText = "codingDisplay";

            Test(
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
        public void GivenACodeableConceptWithNullCoding_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            Test(cc => cc.Coding = null);
        }

        [Theory]
        [MemberData(nameof(GetMultipleCodingDataSource))]
        public void GivenACodeableConceptWithCodings_WhenConverted_ThenOneOrMultipleTokenSearchValuesShouldBeCreated(params Token[] tokens)
        {
            Test(
                cc => cc.Coding.AddRange(tokens.Select(token => new Coding(token.System, token.Code, token.Text))),
                ValidateToken,
                tokens);
        }

        [Fact]
        public void GivenACodeableConceptWithEmptyCoding_WhenConverted_ThenEmptyCodingShouldBeExcluded()
        {
            const string system = "system";
            const string text = "text";

            Test(
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
        public void GivenACodeableConceptWithPartialCoding_WhenConverted_ThenATokenSearchValueShouldBeCreated(string system, string code, string text)
        {
            Test(
                cc =>
                {
                    cc.Coding.Add(new Coding(system, code, text));
                },
                ValidateToken,
                new Token(system, code, text));
        }
    }
}
