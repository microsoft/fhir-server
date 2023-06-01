// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Resources.Upsert;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Features.Validation.Narratives;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources.Upsert
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Validate)]
    public class UpsertResourceValidatorTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1+1")]
        [InlineData("11|")]
        [InlineData("00000000000000000000000000000000000000000000000000000000000000065")]
        public void GivenAResourceWithoutInvalidId_WhenValidatingUpsert_ThenInvalidShouldBeReturned(string id)
        {
            var contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            var profileValidator = Substitute.For<IProfileValidator>();
            var config = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            config.Value.Returns(new CoreFeatureConfiguration());
            contextAccessor.RequestContext.RequestHeaders.Returns(new Dictionary<string, StringValues>());
            var validator = new UpsertResourceValidator(
                new ModelAttributeValidator(),
                new NarrativeHtmlSanitizer(NullLogger<NarrativeHtmlSanitizer>.Instance),
                profileValidator,
                contextAccessor,
                config);
            var resource = Samples.GetDefaultObservation()
                .UpdateId(id);

            var upsertResourceRequest = new UpsertResourceRequest(resource);
            var result = validator.Validate(upsertResourceRequest);
            Assert.False(result.IsValid);
        }

        [InlineData(true, null, true)]
        [InlineData(true, false, false)]
        [InlineData(true, true, true)]
        [InlineData(false, null, false)]
        [InlineData(false, false, false)]
        [InlineData(false, true, true)]
        [Theory]
        public void GivenConfigOrHeader_WhenValidatingUpsert_ThenProfileValidationShouldOrShouldntBeCalled(bool configValue, bool? headerValue, bool shouldCallProfileValidation)
        {
            var contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            var profileValidator = Substitute.For<IProfileValidator>();
            var config = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            config.Value.Returns(new CoreFeatureConfiguration() { ProfileValidationOnUpdate = configValue });
            var headers = new Dictionary<string, StringValues>();
            if (headerValue != null)
            {
                headers.Add(KnownHeaders.ProfileValidation, new StringValues(headerValue.Value.ToString()));
            }

            contextAccessor.RequestContext.RequestHeaders.Returns(headers);
            var validator = new UpsertResourceValidator(
                new ModelAttributeValidator(),
                new NarrativeHtmlSanitizer(NullLogger<NarrativeHtmlSanitizer>.Instance),
                profileValidator,
                contextAccessor,
                config);
            var resource = Samples.GetDefaultObservation();

            var upsertResourceRequest = new UpsertResourceRequest(resource, bundleOperationId: null);
            validator.Validate(upsertResourceRequest);

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
