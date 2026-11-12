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
        private const string ParamName = "value";
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
            Assert.Throws<ArgumentNullException>(ParamName, () => _referenceSearchValueParser.Parse(null, ReferenceParseContext.ResourceReference));
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        public void GivenAnInvalidString_WhenParsing_ThenExceptionShouldBeThrown(string s)
        {
            Assert.Throws<ArgumentException>(ParamName, () => _referenceSearchValueParser.Parse(s, ReferenceParseContext.ResourceReference));
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
            ReferenceSearchValue value = _referenceSearchValueParser.Parse(reference, ReferenceParseContext.ResourceReference);

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
            ReferenceSearchValue value = parser.Parse("https://localhost/stu3/Observation/abc", ReferenceParseContext.ResourceReference);

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
            ReferenceSearchValue value = parser.Parse("https://external-server.com/fhir/Observation/xyz", ReferenceParseContext.ResourceReference);

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
            ReferenceSearchValue value = parser.Parse("Patient/123", ReferenceParseContext.ResourceReference);

            // Assert - Should parse as relative reference
            Assert.NotNull(value);
            Assert.Null(value.BaseUri);
            Assert.Equal(ResourceType.Patient.ToString(), value.ResourceType);
            Assert.Equal("123", value.ResourceId);
        }

        [Theory]
        [InlineData(false, 63)]
        [InlineData(false, 64)]
        [InlineData(true, 64)]
        [InlineData(true, 65)]
        [InlineData(true, 127)]
        [InlineData(true, 128)]
        public void GivenARelativeReferenceWithAnIdWithinThePolicy_WhenParsing_ThenTheFullIdIsPreserved(bool useLongResourceIds, int length)
        {
            // Arrange
            string resourceId = CreateResourceId(length);
            ReferenceSearchValueParser parser = CreateParser(ResourceIdPolicy.From(useLongResourceIds));

            // Act
            ReferenceSearchValue value = parser.Parse($"Patient/{resourceId}", ReferenceParseContext.ResourceReference);

            // Assert
            Assert.Equal(ReferenceKind.InternalOrExternal, value.Kind);
            Assert.Null(value.BaseUri);
            Assert.Equal(ResourceType.Patient.ToString(), value.ResourceType);
            Assert.Equal(resourceId, value.ResourceId);
        }

        [Theory]
        [InlineData(false, 65)]
        [InlineData(false, 129)]
        [InlineData(true, 129)]
        [InlineData(true, 200)]
        public void GivenARelativeReferenceWithAnIdOverThePolicy_WhenParsing_ThenItIsRejectedWithoutTheValue(bool useLongResourceIds, int length)
        {
            // Arrange
            string resourceId = CreateResourceId(length);
            ReferenceSearchValueParser parser = CreateParser(ResourceIdPolicy.From(useLongResourceIds));

            // Act
            FormatException exception = Assert.ThrowsAny<FormatException>(() => parser.Parse($"Patient/{resourceId}", ReferenceParseContext.ResourceReference));

            // Assert
            Assert.DoesNotContain(resourceId[..32], exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(false, 63, "", "/_history/version-2", ReferenceKind.InternalOrExternal, null)]
        [InlineData(false, 64, "https://localhost/stu3/", "", ReferenceKind.Internal, null)]
        [InlineData(false, 64, "https://localhost/stu3/", "/_history/version-2", ReferenceKind.Internal, null)]
        [InlineData(false, 64, "https://external-server.com/fhir/", "", ReferenceKind.External, "https://external-server.com/fhir/")]
        [InlineData(false, 63, "https://external-server.com/fhir/", "/_history/version-2", ReferenceKind.External, "https://external-server.com/fhir/")]
        [InlineData(true, 127, "", "/_history/version-2", ReferenceKind.InternalOrExternal, null)]
        [InlineData(true, 128, "https://localhost/stu3/", "", ReferenceKind.Internal, null)]
        [InlineData(true, 128, "https://localhost/stu3/", "/_history/version-2", ReferenceKind.Internal, null)]
        [InlineData(true, 128, "https://external-server.com/fhir/", "/_history/version-2", ReferenceKind.External, "https://external-server.com/fhir/")]
        public void GivenAReferenceWithAnIdWithinThePolicy_WhenParsing_ThenTheFullIdAndClassificationArePreserved(
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
            ReferenceSearchValue value = parser.Parse($"{referenceBaseUri}Patient/{resourceId}{history}", ReferenceParseContext.ResourceReference);

            // Assert
            Assert.Equal(expectedKind, value.Kind);
            Assert.Equal(expectedBaseUri == null ? null : new Uri(expectedBaseUri), value.BaseUri);
            Assert.Equal(ResourceType.Patient.ToString(), value.ResourceType);
            Assert.Equal(resourceId, value.ResourceId);
        }

        [Theory]
        [InlineData(false, 65, "", "/_history/version-2")]
        [InlineData(false, 65, "https://localhost/stu3/", "")]
        [InlineData(false, 65, "https://localhost/stu3/", "/_history/version-2")]
        [InlineData(false, 65, "https://external-server.com/fhir/", "")]
        [InlineData(false, 129, "https://external-server.com/fhir/", "/_history/version-2")]
        [InlineData(true, 129, "", "/_history/version-2")]
        [InlineData(true, 129, "https://localhost/stu3/", "")]
        [InlineData(true, 129, "https://external-server.com/fhir/", "")]
        [InlineData(true, 129, "https://external-server.com/fhir/", "/_history/version-2")]
        public void GivenAReferenceWithAnIdOverThePolicy_WhenParsing_ThenItIsRejectedWithoutTheValue(
            bool useLongResourceIds,
            int length,
            string referenceBaseUri,
            string history)
        {
            // Arrange
            string resourceId = CreateResourceId(length);
            ReferenceSearchValueParser parser = CreateParser(ResourceIdPolicy.From(useLongResourceIds));

            // Act
            FormatException exception = Assert.ThrowsAny<FormatException>(() => parser.Parse($"{referenceBaseUri}Patient/{resourceId}{history}", ReferenceParseContext.ResourceReference));

            // Assert
            Assert.DoesNotContain(resourceId[..32], exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("Patient/abc_def")]
        [InlineData("Patient/abc$def")]
        [InlineData("https://localhost/stu3/Patient/abc_def")]
        [InlineData("https://external-server.com/fhir/Patient/abc_def/_history/1")]
        public void GivenAReferenceWithAMalformedIdSegment_WhenParsing_ThenItIsRejectedRatherThanShortened(string reference)
        {
            // Act
            FormatException exception = Assert.ThrowsAny<FormatException>(() => _referenceSearchValueParser.Parse(reference, ReferenceParseContext.ResourceReference));

            // Assert
            Assert.DoesNotContain("abc", exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("scheme://test.com/Appointment/")]
        [InlineData("http://example.org/fhir/ValueSet-binding|1.0.0")]
        [InlineData("urn:oid:1.2.3.")]
        public void GivenALongUnrelatedLiteral_WhenParsing_ThenTheCompleteValueIsPreserved(string prefix)
        {
            // Arrange
            string literal = prefix + CreateResourceId(200);

            // Act
            ReferenceSearchValue value = _referenceSearchValueParser.Parse(literal, ReferenceParseContext.ResourceReference);

            // Assert
            Assert.Null(value.ResourceType);
            Assert.Null(value.BaseUri);
            Assert.Equal(literal, value.ResourceId);
        }

        [Theory]
        [InlineData(false, 63)]
        [InlineData(false, 64)]
        [InlineData(true, 128)]
        public void GivenAnIdPrimitiveWithinThePolicy_WhenParsing_ThenTheFullIdIsPreserved(bool useLongResourceIds, int length)
        {
            string resourceId = CreateResourceId(length);
            ReferenceSearchValueParser parser = CreateParser(ResourceIdPolicy.From(useLongResourceIds));

            ReferenceSearchValue value = parser.Parse(resourceId, ReferenceParseContext.ResourceIdPrimitive);

            Assert.Null(value.ResourceType);
            Assert.Equal(resourceId, value.ResourceId);
        }

        [Theory]
        [InlineData(false, 65)]
        [InlineData(true, 129)]
        public void GivenAnIdPrimitiveOverThePolicy_WhenParsing_ThenItIsRejectedWithoutTheValue(bool useLongResourceIds, int length)
        {
            string resourceId = CreateResourceId(length);
            ReferenceSearchValueParser parser = CreateParser(ResourceIdPolicy.From(useLongResourceIds));

            ResourceIdentityFormatException exception = Assert.Throws<ResourceIdentityFormatException>(
                () => parser.Parse(resourceId, ReferenceParseContext.ResourceIdPrimitive));

            Assert.Equal(ResourceIdPolicy.From(useLongResourceIds).MaxLength, exception.MaximumLength);
            Assert.DoesNotContain(resourceId[..32], exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(ReferenceParseContext.ResourceReference)]
        [InlineData(ReferenceParseContext.QueryReference)]
        public void GivenABareIdOverThePolicy_WhenParsingAReference_ThenItIsRejected(ReferenceParseContext context)
        {
            string resourceId = CreateResourceId(65);

            Assert.Throws<ResourceIdentityFormatException>(() => _referenceSearchValueParser.Parse(resourceId, context));
        }

        [Theory]
        [InlineData("http://hl7.org/fhir/ValueSet/example|4.0.1", ReferenceKind.External, "http://hl7.org/fhir/")]
        [InlineData("http://hl7.org/fhir/ValueSet/example#fragment", ReferenceKind.External, "http://hl7.org/fhir/")]
        [InlineData("https://localhost/stu3/ValueSet/example|1", ReferenceKind.Internal, null)]
        [InlineData("ValueSet/example|1", ReferenceKind.InternalOrExternal, null)]
        public void GivenAVersionedCanonical_WhenParsing_ThenTheResourceIdentityIsResolved(string canonical, ReferenceKind expectedKind, string expectedBaseUri)
        {
            ReferenceSearchValue value = _referenceSearchValueParser.Parse(canonical, ReferenceParseContext.CanonicalOrUri);

            Assert.Equal(expectedKind, value.Kind);
            Assert.Equal(expectedBaseUri == null ? null : new Uri(expectedBaseUri), value.BaseUri);
            Assert.Equal("ValueSet", value.ResourceType);
            Assert.Equal("example", value.ResourceId);
        }

        [Fact]
        public void GivenAVersionedQueryReference_WhenParsing_ThenTheResourceIdentityIsResolved()
        {
            ReferenceSearchValue value = _referenceSearchValueParser.Parse("ValueSet/example|1.0", ReferenceParseContext.QueryReference);

            Assert.Equal("ValueSet", value.ResourceType);
            Assert.Equal("example", value.ResourceId);
        }

        [Fact]
        public void GivenAReferenceWithAQueryTrailer_WhenParsing_ThenTheTrailerIsNotPartOfTheId()
        {
            ReferenceSearchValue value = _referenceSearchValueParser.Parse(
                "https://external-server.com/fhir/Patient/123?_format=json",
                ReferenceParseContext.ResourceReference);

            Assert.Equal(ReferenceKind.External, value.Kind);
            Assert.Equal("Patient", value.ResourceType);
            Assert.Equal("123", value.ResourceId);
        }

        [Theory]
        [InlineData("http://example.org/fhir/ValueSet/my_value_set|1.0")]
        [InlineData("my-unversioned-canonical")]
        [InlineData("#contained")]
        public void GivenACanonicalThatIsNotAResourceIdentity_WhenParsing_ThenTheCompleteValueIsALiteral(string canonical)
        {
            ReferenceSearchValue value = _referenceSearchValueParser.Parse(canonical, ReferenceParseContext.CanonicalOrUri);

            Assert.Null(value.ResourceType);
            Assert.Null(value.BaseUri);
            Assert.Equal(canonical, value.ResourceId);
        }

        [Fact]
        public void GivenALongBareCanonical_WhenParsing_ThenTheCompleteValueIsALiteral()
        {
            string canonical = CreateResourceId(200);

            ReferenceSearchValue value = _referenceSearchValueParser.Parse(canonical, ReferenceParseContext.CanonicalOrUri);

            Assert.Null(value.ResourceType);
            Assert.Equal(canonical, value.ResourceId);
        }

        [Fact]
        public void GivenACanonicalWithAnIdShapedSegmentOverThePolicy_WhenParsing_ThenItIsRejectedRatherThanShortened()
        {
            string resourceId = CreateResourceId(65);

            ResourceIdentityFormatException exception = Assert.Throws<ResourceIdentityFormatException>(
                () => _referenceSearchValueParser.Parse($"http://hl7.org/fhir/ValueSet/{resourceId}|1.0", ReferenceParseContext.CanonicalOrUri));

            Assert.DoesNotContain(resourceId[..32], exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenAnUndefinedContext_WhenParsing_ThenExceptionShouldBeThrown()
        {
            Assert.ThrowsAny<ArgumentException>(() => _referenceSearchValueParser.Parse("Patient/123", (ReferenceParseContext)42));
        }

        private static string CreateResourceId(int length)
            => string.Concat(Enumerable.Range(0, length).Select(i => (char)('A' + (i % 26))));

        private ReferenceSearchValueParser CreateParser(ResourceIdPolicy policy)
            => new ReferenceSearchValueParser(_fhirRequestContextAccessor, _instanceConfig, policy);
    }
}
