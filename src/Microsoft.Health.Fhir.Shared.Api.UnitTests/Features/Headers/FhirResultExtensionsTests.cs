// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Microsoft.Net.Http.Headers;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Headers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class FhirResultExtensionsTests
    {
        private const string ETagFormat = "W/\"{0}\"";
        private readonly ResourceElement _mockResource;

        public FhirResultExtensionsTests()
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
            }.ToResourceElement();
        }

        [Fact]
        public void WhenSettingALocationHeader_ThenFhirResultHasALocationHeader()
        {
            var locationUrl = new Uri($"http://localhost/{_mockResource.InstanceType}/{_mockResource.Id}/_history/{_mockResource.VersionId}");

            var urlResolver = Substitute.For<IUrlResolver>();
            urlResolver.ResolveResourceUrl(Arg.Any<ResourceElement>(), Arg.Any<bool>()).Returns(locationUrl);

            var fhirResult = FhirResult.Create(_mockResource).SetLocationHeader(urlResolver);

            Assert.Equal(locationUrl.AbsoluteUri, fhirResult.Headers[HeaderNames.Location]);
        }

        [Fact]
        public void WhenSettingAnETagHeader_ThenFhirResultHasAnETagHeader()
        {
            var version = _mockResource.VersionId;
            var fhirResult = FhirResult.Create(_mockResource).SetETagHeader();

            Assert.Equal(string.Format(ETagFormat, version), fhirResult.Headers[HeaderNames.ETag]);
        }

        [Fact]
        public void WhenSettingALastModifiedHeader_ThenFhirResultHasALastModifierHeader()
        {
            var fhirResult = FhirResult.Create(_mockResource).SetLastModifiedHeader();

            Assert.Equal(_mockResource.LastUpdated?.ToString("r", CultureInfo.InvariantCulture), fhirResult.Headers[HeaderNames.LastModified]);
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
            FhirResult.Create(_mockResource)
                .SetLastModifiedHeader()
                .SetLastModifiedHeader();
        }

        [Fact]
        public void WhenAddingStringEtag_ThenStringETagIsReturned()
        {
            var fhirResult = FhirResult.Create(_mockResource).SetETagHeader(WeakETag.FromVersionId("etag"));

            Assert.Equal("W/\"etag\"", fhirResult.Headers[HeaderNames.ETag]);
        }
    }
}
