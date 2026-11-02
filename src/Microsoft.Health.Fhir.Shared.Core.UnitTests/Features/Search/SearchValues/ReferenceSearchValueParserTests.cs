// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchValues
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ReferenceSearchValueParserTests
    {
        private const string ParamNameS = "s";
        private static readonly Uri BaseUri = new Uri("https://localhost/stu3/");

        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        private readonly ReferenceSearchValueParser _referenceSearchValueParser;
        private readonly IFhirServerInstanceConfiguration _instanceConfig;

        public ReferenceSearchValueParserTests()
        {
            // Create a substitute for IFhirRequestContext
            var fhirRequestContext = Substitute.For<IFhirRequestContext>();

            // Configure the BaseUri property on the substitute
            fhirRequestContext.BaseUri.Returns(BaseUri);

            // Assign the substitute to the RequestContext property
            _fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

            // Create a substitute for IFhirServerInstanceConfiguration
            var instanceConfig = Substitute.For<IFhirServerInstanceConfiguration>();
            instanceConfig.BaseUri.Returns(BaseUri);

            _instanceConfig = instanceConfig;
            _referenceSearchValueParser = CreateParser(ResourceIdPolicy.Standard);
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

        [Fact]
        public void GivenAValidReferenceWhenRequestContextIsNull_WhenParsing_ThenFallsBackToInstanceConfigurationAsInternal()
        {
            // Arrange
            var nullContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            nullContextAccessor.RequestContext.Returns((IFhirRequestContext)null);

            var baseUri = new Uri("https://localhost/stu3/");
            var instanceConfig = Substitute.For<IFhirServerInstanceConfiguration>();
            instanceConfig.BaseUri.Returns(baseUri);

            var parser = new ReferenceSearchValueParser(nullContextAccessor, instanceConfig, ResourceIdPolicy.Standard);

            // Act - Use an internal reference that matches the instance configuration base URI
            ReferenceSearchValue value = parser.Parse("https://localhost/stu3/Observation/abc");

            // Assert - Should be recognized as internal because it matches the base URI
            Assert.NotNull(value);
            Assert.Equal(ReferenceKind.Internal, value.Kind);
            Assert.Equal(ResourceType.Observation.ToString(), value.ResourceType);
            Assert.Equal("abc", value.ResourceId);
        }

        [Fact]
        public void GivenAnExternalReferenceWhenRequestContextIsNull_WhenParsing_ThenFallsBackToInstanceConfigurationAsExternal()
        {
            // Arrange
            var nullContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            nullContextAccessor.RequestContext.Returns((IFhirRequestContext)null);

            var baseUri = new Uri("https://localhost/stu3/");
            var instanceConfig = Substitute.For<IFhirServerInstanceConfiguration>();
            instanceConfig.BaseUri.Returns(baseUri);

            var parser = new ReferenceSearchValueParser(nullContextAccessor, instanceConfig, ResourceIdPolicy.Standard);

            // Act - Use an external reference that does NOT match the instance configuration base URI
            ReferenceSearchValue value = parser.Parse("https://external-server.com/fhir/Observation/xyz");

            // Assert - Should be recognized as external because it doesn't match the base URI
            Assert.NotNull(value);
            Assert.Equal(ReferenceKind.External, value.Kind);
            Assert.Equal(new Uri("https://external-server.com/fhir/"), value.BaseUri);
            Assert.Equal(ResourceType.Observation.ToString(), value.ResourceType);
            Assert.Equal("xyz", value.ResourceId);
        }

        [Fact]
        public void GivenARelativeReferenceWhenRequestContextIsNull_WhenParsing_ThenParsesAsRelative()
        {
            // Arrange
            var nullContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            nullContextAccessor.RequestContext.Returns((IFhirRequestContext)null);

            var baseUri = new Uri("https://localhost/stu3/");
            var instanceConfig = Substitute.For<IFhirServerInstanceConfiguration>();
            instanceConfig.BaseUri.Returns(baseUri);

            var parser = new ReferenceSearchValueParser(nullContextAccessor, instanceConfig, ResourceIdPolicy.Standard);

            // Act - Use a relative reference
            ReferenceSearchValue value = parser.Parse("Patient/123");

            // Assert - Should parse as relative reference
            Assert.NotNull(value);
            Assert.Null(value.BaseUri);
            Assert.Equal(ResourceType.Patient.ToString(), value.ResourceType);
            Assert.Equal("123", value.ResourceId);
        }

        [Theory]
        [InlineData(false, 63, 63)]
        [InlineData(false, 64, 64)]
        [InlineData(false, 65, 64)]
        [InlineData(false, 100, 64)]
        [InlineData(false, 127, 64)]
        [InlineData(false, 128, 64)]
        [InlineData(false, 129, 64)]
        [InlineData(true, 63, 63)]
        [InlineData(true, 64, 64)]
        [InlineData(true, 65, 65)]
        [InlineData(true, 127, 127)]
        [InlineData(true, 128, 128)]
        [InlineData(true, 129, 128)]
        [InlineData(true, 200, 128)]
        public void GivenARelativeReferenceWithAnIdNearTheLimits_WhenParsing_ThenTheExpectedPrefixIsCaptured(bool useLongResourceIds, int length, int expectedLength)
        {
            // Arrange
            string resourceId = CreateResourceId(length);
            ReferenceSearchValueParser parser = CreateParser(ResourceIdPolicy.From(useLongResourceIds));

            // Act
            ReferenceSearchValue value = parser.Parse($"Patient/{resourceId}");

            // Assert
            Assert.Equal(ReferenceKind.InternalOrExternal, value.Kind);
            Assert.Null(value.BaseUri);
            Assert.Equal(ResourceType.Patient.ToString(), value.ResourceType);
            Assert.Equal(resourceId[..expectedLength], value.ResourceId);
        }

        [Theory]
        [InlineData(false, 129, 64, "", "/_history/version-2", ReferenceKind.InternalOrExternal, null)]
        [InlineData(false, 129, 64, "https://localhost/stu3/", "", ReferenceKind.Internal, null)]
        [InlineData(false, 129, 64, "https://localhost/stu3/", "/_history/version-2", ReferenceKind.Internal, null)]
        [InlineData(false, 129, 64, "https://external-server.com/fhir/", "", ReferenceKind.External, "https://external-server.com/fhir/")]
        [InlineData(false, 129, 64, "https://external-server.com/fhir/", "/_history/version-2", ReferenceKind.External, "https://external-server.com/fhir/")]
        [InlineData(false, 63, 63, "", "/_history/version-2", ReferenceKind.InternalOrExternal, null)]
        [InlineData(false, 63, 63, "https://localhost/stu3/", "/_history/version-2", ReferenceKind.Internal, null)]
        [InlineData(false, 63, 63, "https://external-server.com/fhir/", "/_history/version-2", ReferenceKind.External, "https://external-server.com/fhir/")]
        [InlineData(true, 129, 128, "", "/_history/version-2", ReferenceKind.InternalOrExternal, null)]
        [InlineData(true, 129, 128, "https://localhost/stu3/", "", ReferenceKind.Internal, null)]
        [InlineData(true, 129, 128, "https://localhost/stu3/", "/_history/version-2", ReferenceKind.Internal, null)]
        [InlineData(true, 129, 128, "https://external-server.com/fhir/", "", ReferenceKind.External, "https://external-server.com/fhir/")]
        [InlineData(true, 129, 128, "https://external-server.com/fhir/", "/_history/version-2", ReferenceKind.External, "https://external-server.com/fhir/")]
        [InlineData(true, 127, 127, "", "/_history/version-2", ReferenceKind.InternalOrExternal, null)]
        [InlineData(true, 127, 127, "https://localhost/stu3/", "/_history/version-2", ReferenceKind.Internal, null)]
        [InlineData(true, 127, 127, "https://external-server.com/fhir/", "/_history/version-2", ReferenceKind.External, "https://external-server.com/fhir/")]
        public void GivenAReferenceWithALongId_WhenParsing_ThenThePrefixAndReferenceClassificationArePreserved(
            bool useLongResourceIds,
            int length,
            int expectedLength,
            string referenceBaseUri,
            string history,
            ReferenceKind expectedKind,
            string expectedBaseUri)
        {
            // Arrange
            string resourceId = CreateResourceId(length);
            ReferenceSearchValueParser parser = CreateParser(ResourceIdPolicy.From(useLongResourceIds));

            // Act
            ReferenceSearchValue value = parser.Parse($"{referenceBaseUri}Patient/{resourceId}{history}");

            // Assert
            Assert.Equal(expectedKind, value.Kind);
            Assert.Equal(expectedBaseUri == null ? null : new Uri(expectedBaseUri), value.BaseUri);
            Assert.Equal(ResourceType.Patient.ToString(), value.ResourceType);
            Assert.Equal(resourceId[..expectedLength], value.ResourceId);
        }

        private static string CreateResourceId(int length)
            => string.Concat(Enumerable.Range(0, length).Select(i => (char)('A' + (i % 26))));

        private ReferenceSearchValueParser CreateParser(ResourceIdPolicy policy)
            => new ReferenceSearchValueParser(_fhirRequestContextAccessor, _instanceConfig, policy);
    }
}
