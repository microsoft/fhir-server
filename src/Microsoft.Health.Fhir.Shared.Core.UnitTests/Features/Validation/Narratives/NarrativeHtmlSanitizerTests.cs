// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Validation.Narratives;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
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
            _sanitizer = CreateSanitizer();
        }

        private static NarrativeHtmlSanitizer CreateSanitizer(bool rejectDangerousHrefs = false, ILogger<NarrativeHtmlSanitizer> logger = null)
        {
            var config = new CoreFeatureConfiguration { RejectDangerousNarrativeHrefs = rejectDangerousHrefs };
            return new NarrativeHtmlSanitizer(
                logger ?? NullLogger<NarrativeHtmlSanitizer>.Instance,
                Options.Create(config));
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
        public void GivenHtmlWithEmptyDiv_WhenSanitizingHtml_ThenAValidationErrorIsReturned(string val)
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
        public void GivenHtmlWithDivAndText_WhenSanitizingHtml_ThenValidationIsSuccessful(string val)
        {
            var results = _sanitizer.Validate(val);

            Assert.Empty(results);
        }

        [Fact]
        public void GivenExampleNarrativeHtml_WhenSanitizingHtml_ThenValidationIsSuccessful()
        {
            var example = Samples.GetJsonSample<Basic>("BasicExampleNarrative");

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

        [Theory]
        [InlineData("<div><a href=\"javascript:alert('XSS')\">click</a></div>")]
        [InlineData("<div><a href=\"JAVASCRIPT:alert('XSS')\">click</a></div>")]
        [InlineData("<div><a href=\"vbscript:MsgBox('XSS')\">click</a></div>")]
        [InlineData("<div><a href=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgnWFNTJyk8L3NjcmlwdD4=\">click</a></div>")]
        public void GivenHtmlWithDangerousHref_WhenValidating_ThenWarningIsLoggedButValidationSucceeds(string html)
        {
            var results = _sanitizer.Validate(html);

            Assert.Empty(results);
        }

        [Fact]
        public void GivenHtmlWithDangerousHref_WhenValidatingInWarnMode_ThenWarningIsLogged()
        {
            var logger = Substitute.For<ILogger<NarrativeHtmlSanitizer>>();
            var sanitizer = CreateSanitizer(rejectDangerousHrefs: false, logger: logger);

            var results = sanitizer.Validate("<div><a href=\"javascript:alert('XSS')\">click</a></div>");

            Assert.Empty(results);
            logger.Received(1).Log(
                LogLevel.Warning,
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Any<Exception>(),
                Arg.Any<Func<object, Exception, string>>());
        }

        [Theory]
        [InlineData("<div><a href=\"javascript:alert('XSS')\">click</a></div>")]
        [InlineData("<div><a href=\"JAVASCRIPT:alert('XSS')\">click</a></div>")]
        [InlineData("<div><a href=\"vbscript:MsgBox('XSS')\">click</a></div>")]
        [InlineData("<div><a href=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgnWFNTJyk8L3NjcmlwdD4=\">click</a></div>")]
        public void GivenHtmlWithDangerousHref_WhenValidatingInRejectMode_ThenValidationErrorIsReturned(string html)
        {
            var sanitizer = CreateSanitizer(rejectDangerousHrefs: true);

            var results = sanitizer.Validate(html);

            Assert.NotEmpty(results);
        }

        [Theory]
        [InlineData("<div><a href=\"https://example.com\">safe</a></div>")]
        [InlineData("<div><a href=\"http://example.com\">safe</a></div>")]
        [InlineData("<div><a href=\"#section1\">anchor</a></div>")]
        [InlineData("<div><a href=\"mypage.html\">relative</a></div>")]
        [InlineData("<div><a href=\"mailto:user@example.com\">email</a></div>")]
        public void GivenHtmlWithSafeHref_WhenValidating_ThenValidationIsSuccessful(string html)
        {
            var results = _sanitizer.Validate(html);

            Assert.Empty(results);
        }

        [Theory]
        [InlineData("<div><a href=\"javascript:alert('XSS')\">click</a></div>", "<div><a>click</a></div>")]
        [InlineData("<div><a href=\"data:text/html,<script>alert(1)</script>\">click</a></div>", "<div><a>click</a></div>")]
        [InlineData("<div><a href=\"https://example.com\">safe</a></div>", "<div><a href=\"https://example.com\">safe</a></div>")]
        public void GivenHtmlWithMaliciousHref_WhenSanitizing_ThenHrefIsRemoved(string input, string output)
        {
            var results = _sanitizer.Sanitize(input);

            Assert.Equal(output, results);
        }
    }
}
