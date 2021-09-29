// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Resources.Create;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Features.Validation.Narratives;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources.Create
{
    public class CreateResourceValidatorTests
    {
        [Theory]
        [InlineData("")]
        [InlineData("1+1")]
        [InlineData("11|")]
        [InlineData("00000000000000000000000000000000000000000000000000000000000000065")]
        public void GivenAResourceWithInvalidId_WhenValidatingUpsert_ThenInvalidShouldBeReturned(string id)
        {
            var contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            var profileValidator = Substitute.For<IProfileValidator>();
            var config = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            config.Value.Returns(new CoreFeatureConfiguration());
            contextAccessor.RequestContext.RequestHeaders.Returns(new Dictionary<string, StringValues>());
            var validator = new CreateResourceValidator(
                new ModelAttributeValidator(),
                new NarrativeHtmlSanitizer(NullLogger<NarrativeHtmlSanitizer>.Instance),
                profileValidator,
                contextAccessor,
                config);

            var defaultObservation = Samples.GetDefaultObservation().ToPoco<Observation>();
            defaultObservation.Text.Div = MaliciousNarrative().ToString();

            var defaultPatient = Samples.GetDefaultPatient().ToPoco<Patient>();
            defaultPatient.Text.Div = MaliciousNarrative().ToString();

            var bundle = new Bundle();
            bundle.Entry.Add(new Bundle.EntryComponent { Resource = defaultObservation });
            bundle.Entry.Add(new Bundle.EntryComponent { Resource = defaultPatient });

            var resource = bundle.ToResourceElement()
                            .UpdateId(id);

            var createResourceRequest = new CreateResourceRequest(resource);
            var result = validator.Validate(createResourceRequest);
            Assert.False(result.IsValid);
            Assert.True(result.Errors.Count >= 3);
            Assert.NotEmpty(result.Errors.Where(e => e.ErrorMessage.Contains("min. cardinality 1 cannot be null")));
            Assert.NotEmpty(result.Errors.Where(e => e.ErrorMessage.Contains("XHTML content should be contained within a single <div> element")));
            Assert.NotEmpty(result.Errors.Where(e => e.ErrorMessage.Contains("Id must be any combination of upper or lower case ASCII letters")));
        }

        [InlineData(true, null, true)]
        [InlineData(true, false, false)]
        [InlineData(true, true, true)]
        [InlineData(false, null, false)]
        [InlineData(false, false, false)]
        [InlineData(false, true, true)]
        [Theory]
        public void GivenConfigOrHeader_WhenValidatingCreate_ThenProfileValidationShouldOrShouldntBeCalled(bool configValue, bool? headerValue, bool shouldCallProfileValidation)
        {
            var contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            var profileValidator = Substitute.For<IProfileValidator>();
            var config = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            config.Value.Returns(new CoreFeatureConfiguration() { ProfileValidationOnCreate = configValue });
            var headers = new Dictionary<string, StringValues>();
            if (headerValue != null)
            {
                headers.Add(KnownHeaders.ProfileValidation, new StringValues(headerValue.Value.ToString()));
            }

            contextAccessor.RequestContext.RequestHeaders.Returns(headers);
            var validator = new CreateResourceValidator(
                new ModelAttributeValidator(),
                new NarrativeHtmlSanitizer(NullLogger<NarrativeHtmlSanitizer>.Instance),
                profileValidator,
                contextAccessor,
                config);
            var resource = Samples.GetDefaultObservation();

            var createResourceRequest = new CreateResourceRequest(resource);
            validator.Validate(createResourceRequest);

            if (shouldCallProfileValidation)
            {
                profileValidator.Received().TryValidate(Arg.Any<ITypedElement>(), Arg.Any<string>());
            }
            else
            {
                profileValidator.DidNotReceive().TryValidate(Arg.Any<ITypedElement>(), Arg.Any<string>());
            }
        }

        public static IEnumerable<object[]> MaliciousNarrative()
        {
            return new object[]
            {
                            "<div>'';!--\"<XSS>=&{()}</div>",
                            "<div><?xml version=\"1.0\" encoding=\"ISO-8859-1\"?><foo><![CDATA[<]]>SCRIPT<![CDATA[>]]>alert('gotcha');<![CDATA[<]]>/SCRIPT<![CDATA[>]]></foo></div>",
                            "<div><a onclick=\"alert('gotcha');\"></a></div>",
                            "<div><div id=\"nested\"><span><a onclick=\"alert('gotcha');\"></a></span></div></div>",
                            "<div><IMG SRC=\"jav &#x0D;ascript:alert(<WBR>'XSS');\"></div>",
                            "<div><img%20src%3D%26%23x6a;%26%23x61;%26%23x76;%26%23x61;%26%23x73;%26%23x63;%26%23x72;%26%23x69;%26%23x70;%26%23x74;%26%23x3a;alert(%26quot;%26%23x20;XSS%26%23x20;Test%26%23x20;Successful%26quot;)></div>",
                            "<div><IMGSRC=&#106;&#97;&#118;&#97;&<WBR>#115;&#99;&#114;&#105;&#112;&<WBR>#116;&#58;&#97;&#108;&#101;&<WBR>#114;&#116;&#40;&#39;&#88;&#83<WBR>;&#83;&#39;&#41></div>",
                            "<div><script src=http://www.example.com/malicious-code.js></script></div>",
                            "<div>%22%27><img%20src%3d%22javascript:alert(%27%20XSS%27)%22></div>",
                            "<div>\"'><img%20src%3D%26%23x6a;%26%23x61;%26%23x76;%26%23x61;%26%23x73;%26%23x63;%26%23x72;%26%23x69;%26%23x70;%26%23x74;%26%23x3a;</div>",
                            "<div>\"><script>alert(\"XSS\")</script>&</div>",
                            "<div>\"><STYLE>@import\"javascript:alert('XSS')\";</ STYLE ></div>",
                            "<div>http://www.example.com/>\"><script>alert(\"XSS\")</script>&</div>",
                            "<div onmouseover=\"alert('gotcha');\"></div>",
            }.Select(x => new[] { x });
        }
    }
}
