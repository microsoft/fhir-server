// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Validation.Narratives;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Validation.Narratives
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class NarrativeHtmlSanitizerTests : NarrativeDataTestBase
    {
        private readonly NarrativeHtmlSanitizer _sanitizer;

        public NarrativeHtmlSanitizerTests()
        {
            _sanitizer = new NarrativeHtmlSanitizer(NullLogger<NarrativeHtmlSanitizer>.Instance);
        }

        [Theory]
        [MemberData(nameof(XssStrings))]
        public void Validate(string code)
        {
            var results = _sanitizer.Validate(code);

            Assert.NotEmpty(results);
        }

        [Theory]
        [InlineData("<div></div>")]
        [InlineData("<div>     </div>")]
        [InlineData("<h1>Example!</h1>")]
        [InlineData("<?xml version=\"1.0\" encoding=\"UTF-8\"?><div>text</div>")]
        [InlineData("<div>text</div><?xml version=\"1.0\" encoding=\"UTF-8\"?>")]
        [InlineData("<div>text</div><?xml version=\"1.0\" encoding=\"UTF-8\"?>div>")]
        [InlineData("<not_real>Example!</not_real>")]
        [InlineData("<?not_real><div>Example!</div>")]
        [InlineData("<div><!-- Comment ended with a dash. This error should be ignored ---><not_real>This tag should return validation error</not_real></div>")]
        public void GivenInvalidNarrativeHtml_WhenSanitizingHtml_ThenAValidationErrorIsReturned(string val)
        {
            var results = _sanitizer.Validate(val);

            Assert.NotEmpty(results);
        }

        [Theory]
        [InlineData("<div>Example!</div>")]
        [InlineData("<div><p></div>")]
        [InlineData("<div>Test<div /></div>")]
        [InlineData("<div><table><tr><td>Test</td></tr><tr><td /></tr></table></div>")]
        [InlineData("               <div><p></div>")]
        [InlineData("<div><img src=\"test.png\" />")]
        public void GivenHtmlWithDivAndText_WhenSanitizingHtml_ThenValidationIsSuccessful(string val)
        {
            var results = _sanitizer.Validate(val);

            Assert.Empty(results);
        }

        [Theory]
        [InlineData("BasicExampleNarrative")]
        [InlineData("StructureDefinition-us-core-birthsex")]
        public void GivenExampleNarrativeHtml_WhenSanitizingHtml_ThenValidationIsSuccessful(string name)
        {
            var example = Samples.GetJsonSample<DomainResource>(name);

            var results = _sanitizer.Validate(example.Text.Div);

            Assert.Empty(results);
        }

        [Theory]
        [InlineData("<div>Example!</div>", "<div>Example!</div>")]
        [InlineData("<div><p></div>", "<div><p></p></div>")]
        [InlineData("<div><script></script></div>", "<div></div>")]
        [InlineData("<div><script>Text</script></div>", "<div>Text</div>")]
        [InlineData("<div><a onclick=\"javascript:\">test</a></div>", "<div><a>test</a></div>")]
        [InlineData("<div><a href=\"mypage.html\">test</a></div>", "<div><a href=\"mypage.html\">test</a></div>")]
        public void GivenHtmlWithDivAndText_WhenCleaningHtml_ThenResultIsSuccessful(string input, string output)
        {
            var results = _sanitizer.Sanitize(input);

            Assert.Equal(output, results);
        }
    }
}
