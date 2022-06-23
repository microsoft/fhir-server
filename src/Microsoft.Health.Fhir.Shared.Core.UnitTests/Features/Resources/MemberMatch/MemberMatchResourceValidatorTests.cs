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
using Microsoft.Health.Fhir.Core.Features.Resources.MemberMatch;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Features.Validation.Narratives;
using Microsoft.Health.Fhir.Core.Messages.MemberMatch;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Validation.Narratives;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources.MemberMatch
{
    public class MemberMatchResourceValidatorTests : NarrativeDataTestBase
    {
#if !Stu3
        [Fact]
        public void GivenAnInvalidResource_WhenValidatingMemberMatch_ThenInvalidShouldBeReturned()
        {
            var contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            var profileValidator = Substitute.For<IProfileValidator>();
            var config = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            config.Value.Returns(new CoreFeatureConfiguration());
            contextAccessor.RequestContext.RequestHeaders.Returns(new Dictionary<string, StringValues>());
            var validator = new MemberMatchResourceValidator(
                new ModelAttributeValidator(),
                new NarrativeHtmlSanitizer(NullLogger<NarrativeHtmlSanitizer>.Instance),
                profileValidator,
                contextAccessor,
                config);

            var defaultCoverage = Samples.GetDefaultCoverage().ToPoco<Coverage>();
            var defaultPatient = Samples.GetDefaultPatient().ToPoco<Patient>();

            defaultCoverage.Status = null;
            var createMemberMatchRequest = new MemberMatchRequest(defaultPatient.ToResourceElement(), defaultCoverage.ToResourceElement());
            var result = validator.Validate(createMemberMatchRequest);
            Assert.False(result.IsValid);
            Assert.True(result.Errors.Count >= 1);
            Assert.NotEmpty(result.Errors.Where(e => e.ErrorMessage.Contains("minimum cardinality 1 cannot be null")));
        }
#endif

        [Theory]
        [InlineData("", nameof(XssStrings))]
        [InlineData("1+1", nameof(XssStrings))]
        [InlineData("11|", nameof(XssStrings))]
        [InlineData("00000000000000000000000000000000000000000000000000000000000000065", nameof(XssStrings))]
        public void GivenAResourceWithInvalidId_WhenValidatingMemberMatch_ThenInvalidShouldBeReturned(string id, string maliciousNarrative)
        {
            var contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            var profileValidator = Substitute.For<IProfileValidator>();
            var config = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            config.Value.Returns(new CoreFeatureConfiguration());
            contextAccessor.RequestContext.RequestHeaders.Returns(new Dictionary<string, StringValues>());
            var validator = new MemberMatchResourceValidator(
                new ModelAttributeValidator(),
                new NarrativeHtmlSanitizer(NullLogger<NarrativeHtmlSanitizer>.Instance),
                profileValidator,
                contextAccessor,
                config);

            var defaultCoverage = Samples.GetDefaultCoverage().ToPoco<Coverage>();
            var defaultPatient = Samples.GetDefaultPatient().ToPoco<Patient>();

            defaultCoverage.Text.Div = maliciousNarrative;
            defaultPatient.Text.Div = maliciousNarrative;

            var coverageResource = defaultCoverage.ToResourceElement()
                            .UpdateId(id);

            var patientResource = defaultPatient.ToResourceElement()
                            .UpdateId(id);

            var createResourceRequest = new MemberMatchRequest(patientResource, coverageResource);
            var result = validator.Validate(createResourceRequest);
            Assert.False(result.IsValid);
            Assert.True(result.Errors.Count >= 2);
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
        public void GivenConfigOrHeader_WhenValidatingMemberMatch_ThenProfileValidationShouldOrShouldntBeCalled(bool configValue, bool? headerValue, bool shouldCallProfileValidation)
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
            var validator = new MemberMatchResourceValidator(
                new ModelAttributeValidator(),
                new NarrativeHtmlSanitizer(NullLogger<NarrativeHtmlSanitizer>.Instance),
                profileValidator,
                contextAccessor,
                config);

            var createMemberMatchRequest = new MemberMatchRequest(Samples.GetDefaultCoverage().ToPoco<Coverage>().ToResourceElement(), Samples.GetDefaultPatient().ToPoco<Patient>().ToResourceElement());
            validator.Validate(createMemberMatchRequest);

            if (shouldCallProfileValidation)
            {
                profileValidator.Received().TryValidate(Arg.Any<ITypedElement>(), Arg.Any<string>());
            }
            else
            {
                profileValidator.DidNotReceive().TryValidate(Arg.Any<ITypedElement>(), Arg.Any<string>());
            }
        }
    }
}
