// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
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

        [Theory]
        [InlineData(false, 65, true, "scheme://test.com/")]
        [InlineData(false, 65, false, "scheme://test.com/")]
        [InlineData(true, 129, true, "scheme://test.com/")]
        [InlineData(true, 129, false, "scheme://test.com/")]
        [InlineData(false, 65, true, "prefix/")]
        [InlineData(false, 65, false, "prefix/")]
        [InlineData(true, 129, true, "prefix/")]
        [InlineData(true, 129, false, "prefix/")]
        public void GivenAnUntypedReferenceWithAnIdOverTheLimit_WhenParsing_ThenTheWholeValueIsReturned(
            bool useLongResourceIds,
            int length,
            bool rejectLongReferences,
            string prefix)
        {
            // Arrange
            string reference = $"{prefix}Appointment/{CreateResourceId(length)}";
            ReferenceSearchValueParser parser = CreateParser(ResourceIdPolicy.From(useLongResourceIds, rejectLongReferences));

            // Act
            ReferenceSearchValue value = parser.Parse(reference);

            // Assert
            Assert.Null(value.ResourceType);
            Assert.Equal(reference, value.ResourceId);
            Assert.Null(value.BaseUri);
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
        [InlineData(false, 64, "", "", ReferenceKind.InternalOrExternal, null)]
        [InlineData(false, 64, "", "/_history/version-2", ReferenceKind.InternalOrExternal, null)]
        [InlineData(false, 64, "https://localhost/stu3/", "/_history/version-2", ReferenceKind.Internal, null)]
        [InlineData(false, 64, "https://external-server.com/fhir/", "", ReferenceKind.External, "https://external-server.com/fhir/")]
        [InlineData(true, 65, "", "", ReferenceKind.InternalOrExternal, null)]
        [InlineData(true, 128, "", "", ReferenceKind.InternalOrExternal, null)]
        [InlineData(true, 128, "https://localhost/stu3/", "/_history/version-2", ReferenceKind.Internal, null)]
        [InlineData(true, 128, "https://external-server.com/fhir/", "/_history/version-2", ReferenceKind.External, "https://external-server.com/fhir/")]
        public void GivenAReferenceWithAnIdWithinTheLimit_WhenParsing_ThenTheWholeIdIsCaptured(
            bool useLongResourceIds,
            int length,
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
            Assert.Equal(resourceId, value.ResourceId);
        }

        [Theory]
        [InlineData(false, 65, "", "")]
        [InlineData(false, 129, "", "/_history/version-2")]
        [InlineData(false, 65, "https://localhost/stu3/", "")]
        [InlineData(false, 65, "https://external-server.com/fhir/", "/_history/version-2")]
        [InlineData(true, 129, "", "")]
        [InlineData(true, 200, "", "/_history/version-2")]
        [InlineData(true, 129, "https://localhost/stu3/", "")]
        [InlineData(true, 129, "https://external-server.com/fhir/", "/_history/version-2")]
        public void GivenAReferenceWithAnIdOverTheLimit_WhenParsing_ThenBadRequestIsThrown(
            bool useLongResourceIds,
            int length,
            string referenceBaseUri,
            string history)
        {
            // Arrange
            ReferenceSearchValueParser parser = CreateParser(ResourceIdPolicy.From(useLongResourceIds));

            // Act and Assert
            Assert.Throws<BadRequestException>(() => parser.Parse($"{referenceBaseUri}Patient/{CreateResourceId(length)}{history}"));
        }

        [Theory]
        [InlineData(false, 65, 64)]
        [InlineData(true, 129, 128)]
        public void GivenRejectionIsDisabled_WhenParsingAReferenceWithAnIdOverTheLimit_ThenTheIdIsCutOffAtTheLimit(bool useLongResourceIds, int length, int expectedLength)
        {
            // Arrange
            string resourceId = CreateResourceId(length);
            ReferenceSearchValueParser parser = CreateParser(ResourceIdPolicy.From(useLongResourceIds, rejectLongReferences: false));

            // Act
            ReferenceSearchValue value = parser.Parse($"Patient/{resourceId}");

            // Assert
            Assert.Equal(resourceId[..expectedLength], value.ResourceId);
        }

        [Fact]
        public void GivenAUrlLongerThanTheLimitThatIsNotAResourceReference_WhenParsing_ThenTheWholeValueIsReturned()
        {
            // Arrange
            string canonical = $"http://example.org/fhir/profiles/{CreateResourceId(100)}|1.0";

            // Act
            ReferenceSearchValue value = _referenceSearchValueParser.Parse(canonical);

            // Assert
            Assert.Null(value.ResourceType);
            Assert.Equal(canonical, value.ResourceId);
        }

        private static string CreateResourceId(int length)
            => string.Concat(Enumerable.Range(0, length).Select(i => (char)('A' + (i % 26))));

        private ReferenceSearchValueParser CreateParser(ResourceIdPolicy policy)
            => new ReferenceSearchValueParser(_fhirRequestContextAccessor, _instanceConfig, policy);
    }
}
