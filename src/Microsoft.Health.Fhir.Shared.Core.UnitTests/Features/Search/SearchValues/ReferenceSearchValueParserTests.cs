// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchValues
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ReferenceSearchValueParserTests
    {
        private const string ParamNameS = "s";
        private static readonly Uri BaseUri = new Uri("https://localhost/stu3/");

        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        private readonly ReferenceSearchValueParser _referenceSearchValueParser;

        public ReferenceSearchValueParserTests()
        {
            _fhirRequestContextAccessor.RequestContext.BaseUri.Returns(BaseUri);

            _referenceSearchValueParser = new ReferenceSearchValueParser(_fhirRequestContextAccessor);
        }

        [Fact]
        public void GivenANullString_WhenParsing_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameS, () => _referenceSearchValueParser.Parse(null));
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        public void GivenAnInvalidString_WhenParsing_ThenExceptionShouldBeThrown(string s)
        {
            Assert.Throws<ArgumentException>(ParamNameS, () => _referenceSearchValueParser.Parse(s));
        }

        [Theory]
        [InlineData("Observation/abc", null, ResourceType.Observation, "abc")]
        [InlineData("http://hl7.fhir.org/stu3/Account/123", "http://hl7.fhir.org/stu3/", ResourceType.Account, "123")]
        [InlineData("Observation", null, null, "Observation")]
        [InlineData("http://test.com/Appointment", null, null, "http://test.com/Appointment")]
        [InlineData("scheme://test.com/Appointment/123", null, null, "scheme://test.com/Appointment/123")]
        [InlineData("http://localhost/Appointment/xyz", "http://localhost/", ResourceType.Appointment, "xyz")]
        [InlineData("hTTpS://LOCALHOST/stu3/Patient/Test", null, ResourceType.Patient, "Test")]
        public void GivenAValidReference_WhenParsing_ThenCorrectSearchValueShouldBeReturned(string reference, string baseUri, ResourceType? resourceType, string resourceId)
        {
            ReferenceSearchValue value = _referenceSearchValueParser.Parse(reference);

            Assert.NotNull(value);
            Assert.Equal(baseUri == null ? null : new Uri(baseUri), value.BaseUri);
            Assert.Equal(resourceType.ToString(), value.ResourceType ?? string.Empty);
            Assert.Equal(resourceId, value.ResourceId);
        }
    }
}
