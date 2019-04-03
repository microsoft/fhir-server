// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Net.Http.Headers;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Headers
{
    public class FhirHeadersTests
    {
        private const string ETagFormat = "W/\"{0}\"";
        private readonly Resource _mockResource;

        public FhirHeadersTests()
        {
            var id = Guid.NewGuid().ToString();
            var version = Guid.NewGuid().ToString();
            var date = new DateTimeOffset(2017, 10, 30, 0, 0, 0, TimeSpan.Zero);

            _mockResource = new Observation
            {
                Id = id,
                Meta = new Meta
                {
                    VersionId = version,
                    LastUpdated = date,
                },
            };
        }

        [Fact]
        public void WhenSettingALocationHeader_ThenFhirResultHasALocationHeader()
        {
            var locationUrl = new Uri($"http://localhost/{_mockResource.GetType().Name}/{_mockResource.Id}/_history/{_mockResource.Meta.VersionId}");
            var urlResolver = Substitute.For<IUrlResolver>();
            urlResolver.ResolveResourceUrl(Arg.Any<Resource>(), Arg.Any<bool>()).Returns(locationUrl);

            var fhirResult = FhirResult.Create(_mockResource).SetLocationHeader(urlResolver);

            Assert.Equal(locationUrl.AbsoluteUri, fhirResult.Headers[HeaderNames.Location]);
        }

        [Fact]
        public void WhenSettingAnETagHeader_ThenFhirResultHasAnETagHeader()
        {
            var version = _mockResource.Meta.VersionId;
            var fhirResult = FhirResult.Create(_mockResource).SetETagHeader();

            Assert.Equal(string.Format(ETagFormat, version), fhirResult.Headers[HeaderNames.ETag]);
        }

        [Fact]
        public void WhenSettingALastModifiedHeader_ThenFhirResultHasALastModifierHeader()
        {
            var fhirResult = FhirResult.Create(_mockResource).SetLastModifiedHeader();

            Assert.Equal(_mockResource.Meta.LastUpdated?.ToString("r", CultureInfo.InvariantCulture), fhirResult.Headers[HeaderNames.LastModified]);
        }

        [Fact]
        public void WhenCreatingAFhirResultAndNotSettingHeaders_ThenThereIsNoHeaders()
        {
            var fhirResult = FhirResult.Create(_mockResource);

            Assert.Empty(fhirResult.Headers);
        }

        [Fact]
        public void WhenAddingTwoHeaders_ThenFhirResultHasAtLeastTwoHeaders()
        {
            var fhirResult = FhirResult.Create(_mockResource).SetLastModifiedHeader().SetETagHeader();

            Assert.Equal(2, fhirResult.Headers.Count);
        }

        [Fact]
        public void WhenAddingSameHeaderTwice_ThenOnlyOneHeaderIsPresent()
        {
            Assert.Throws<ArgumentException>(() => FhirResult.Create(_mockResource).SetLastModifiedHeader().SetLastModifiedHeader());
        }

        [Fact]
        public void WhenAddingStringEtag_ThenStringETagIsReturned()
        {
            var fhirResult = FhirResult.Create(_mockResource).SetETagHeader(WeakETag.FromVersionId("etag"));

            Assert.Equal("W/\"etag\"", fhirResult.Headers[HeaderNames.ETag]);
        }

        [Fact]
        public void WhenSettingAContentLocationHeader_ThenFhirResultHasAContentLocationHeader()
        {
            string opName = OperationsConstants.Export;
            string id = Guid.NewGuid().ToString();
            var operationResultUrl = new Uri($"http://localhost/{OperationsConstants.Operations}/{opName}/{id}");

            var urlResolver = Substitute.For<IUrlResolver>();
            urlResolver.ResolveOperationResultUrl(Arg.Any<string>(), Arg.Any<string>()).Returns(operationResultUrl);

            var fhirResult = new FhirResult().SetContentLocationHeader(urlResolver, opName, id);

            Assert.Equal(operationResultUrl.AbsoluteUri, fhirResult.Headers[HeaderNames.ContentLocation]);
        }
    }
}
