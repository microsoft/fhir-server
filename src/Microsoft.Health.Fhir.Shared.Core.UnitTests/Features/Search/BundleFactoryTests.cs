// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Search
{
    public class BundleFactoryTests
    {
        private readonly FhirJsonSerializer _fhirJsonSerializer = new FhirJsonSerializer();
        private readonly FhirJsonParser _fhirJsonParser = new FhirJsonParser();
        private readonly IUrlResolver _urlResolver = Substitute.For<IUrlResolver>();
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        private readonly ResourceDeserializer _resourceDeserializer;
        private readonly BundleFactory _bundleFactory;

        private readonly string _correlationId;

        public BundleFactoryTests()
        {
            _resourceDeserializer = new ResourceDeserializer(
                (FhirResourceFormat.Json, new Func<string, string, DateTimeOffset, ResourceElement>((str, version, lastUpdated) => _fhirJsonParser.Parse(str).ToResourceElement())));

            _bundleFactory = new BundleFactory(
                _urlResolver,
                _fhirRequestContextAccessor,
                _resourceDeserializer);

            IFhirRequestContext fhirRequestContext = Substitute.For<IFhirRequestContext>();

            _correlationId = Guid.NewGuid().ToString();

            fhirRequestContext.CorrelationId.Returns(_correlationId);

            _fhirRequestContextAccessor.FhirRequestContext.Returns(fhirRequestContext);
        }

        [Fact]
        public void GivenASearchResult_WhenCreateSearchBundle_ThenBundleShouldBeReturned()
        {
            IReadOnlyList<Tuple<string, string>> unsupportedParameters = new Tuple<string, string>[0];
            const string continuationToken = "ct";
            var resourceUrl = new Uri("http://resource");
            var nextUrl = new Uri("http://next");
            var selfUrl = new Uri("http://self");

            _urlResolver.ResolveResourceUrl(Arg.Any<ResourceElement>()).Returns(resourceUrl);
            _urlResolver.ResolveRouteUrl(unsupportedParameters, continuationToken).Returns(nextUrl);
            _urlResolver.ResolveRouteUrl(unsupportedParameters).Returns(selfUrl);

            ResourceElement resourceElement = Samples.GetDefaultObservation().UpdateId("123");

            var resourceWrapper = new ResourceWrapper(
                resourceElement,
                new RawResource(_fhirJsonSerializer.SerializeToString(resourceElement.ToPoco<Observation>()), FhirResourceFormat.Json),
                null,
                false,
                null,
                null,
                null);

            var searchResult = new SearchResult(new[] { resourceWrapper }, unsupportedParameters, continuationToken);

            ResourceElement actual = _bundleFactory.CreateSearchBundle(searchResult);

            Assert.NotNull(actual);
            Assert.Equal(Bundle.BundleType.Searchset.ToString().ToLowerInvariant(), actual.Scalar<string>("Bundle.type"));
            Assert.Equal(_correlationId, actual.Id);
        }
    }
}
