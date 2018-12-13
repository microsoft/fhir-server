// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Resources.Upsert;
using Microsoft.Health.Fhir.Core.Features.Validation.Narratives;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Tests.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources.Upsert
{
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
            var validator = new UpsertResourceValidator(new NarrativeHtmlSanitizer(NullLogger<NarrativeHtmlSanitizer>.Instance));
            Observation resource = Samples.GetDefaultObservation();
            resource.Id = id;
            var upsertResourceRequest = new UpsertResourceRequest(resource);
            var result = validator.Validate(upsertResourceRequest);
            Assert.False(result.IsValid);
        }
    }
}
