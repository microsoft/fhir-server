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
using Microsoft.Health.Fhir.Core.UnitTests.Features.Validation.Narratives;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources.Create
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Validate)]
    public class CreateResourceValidatorTests : NarrativeDataTestBase
    {
        [Theory]
        [InlineData("", nameof(XssStrings))]
        [InlineData("1+1", nameof(XssStrings))]
        [InlineData("11|", nameof(XssStrings))]
        [InlineData("00000000000000000000000000000000000000000000000000000000000000065", nameof(XssStrings))]
        public void GivenAResourceWithInvalidId_WhenValidatingUpsert_ThenInvalidShouldBeReturned(string id, string maliciousNarrative)
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
            defaultObservation.Text.Div = maliciousNarrative;

            var defaultPatient = Samples.GetDefaultPatient().ToPoco<Patient>();
            defaultPatient.Text.Div = maliciousNarrative;

            var bundle = new Bundle();
            bundle.Entry.Add(new Bundle.EntryComponent { Resource = defaultObservation });
            bundle.Entry.Add(new Bundle.EntryComponent { Resource = defaultPatient });

            var resource = bundle.ToResourceElement()
                            .UpdateId(id);

            var createResourceRequest = new CreateResourceRequest(resource);
            var result = validator.Validate(createResourceRequest);
            Assert.False(result.IsValid);
            Assert.True(result.Errors.Count >= 3);
            Assert.NotEmpty(result.Errors.Where(e => e.ErrorMessage.Contains("minimum cardinality 1 cannot be null")));
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
    }
}
