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

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class BundleFactoryTests
    {
        private readonly FhirJsonSerializer _fhirJsonSerializer = new FhirJsonSerializer();
        private readonly FhirJsonParser _fhirJsonParser = new FhirJsonParser();
        private readonly IUrlResolver _urlResolver = Substitute.For<IUrlResolver>();
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        private readonly ResourceDeserializer _resourceDeserializer;
        private readonly BundleFactory _bundleFactory;

        private const string _continuationToken = "ct";
        private const string _resourceUrlFormat = "http://resource/{0}";
        private static readonly string _correlationId = Guid.NewGuid().ToString();
        private static readonly Uri _selfUrl = new Uri("http://self/");
        private static readonly Uri _nextUrl = new Uri("http://next/");
        private static readonly IReadOnlyList<Tuple<string, string>> _unsupportedSearchParameters = new Tuple<string, string>[0];
        private static readonly IReadOnlyList<(string searchParameter, string reason)> _unsupportedSortingParameters = Array.Empty<(string parameterName, string reason)>();

        private static readonly DateTimeOffset _dateTime = new DateTimeOffset(2019, 1, 5, 15, 30, 23, TimeSpan.FromHours(8));

        public BundleFactoryTests()
        {
            _resourceDeserializer = new ResourceDeserializer(
                (FhirResourceFormat.Json, new Func<string, string, DateTimeOffset, ResourceElement>((str, version, lastUpdated) => _fhirJsonParser.Parse(str).ToResourceElement())));

            _bundleFactory = new BundleFactory(
                _urlResolver,
                _fhirRequestContextAccessor,
                _resourceDeserializer);

            IFhirRequestContext fhirRequestContext = Substitute.For<IFhirRequestContext>();

            fhirRequestContext.CorrelationId.Returns(_correlationId);

            _fhirRequestContextAccessor.FhirRequestContext.Returns(fhirRequestContext);
        }

        [Fact]
        public void GivenAnEmptySearchResult_WhenCreateSearchBundle_ThenCorrectBundleShouldBeReturned()
        {
            _urlResolver.ResolveRouteUrl(_unsupportedSearchParameters, _unsupportedSortingParameters).Returns(_selfUrl);

            ResourceElement actual = null;

            using (Mock.Property(() => Clock.UtcNowFunc, () => _dateTime))
            {
                actual = _bundleFactory.CreateSearchBundle(new SearchResult(new ResourceWrapper[0], _unsupportedSearchParameters, _unsupportedSortingParameters, null));
            }

            Assert.NotNull(actual);
            Assert.Equal(Bundle.BundleType.Searchset.ToString().ToLowerInvariant(), actual.Scalar<string>("Bundle.type"));
            Assert.Equal(_correlationId.ToString(), actual.Id);
            Assert.Equal(_dateTime, actual.LastUpdated);
            Assert.Equal(_selfUrl.OriginalString, actual.Scalar<string>("Bundle.link.where(relation='self').url"));
        }

        [Fact]
        public void GivenASearchResult_WhenCreateSearchBundle_ThenCorrectBundleShouldBeReturned()
        {
            _urlResolver.ResolveResourceUrl(Arg.Any<ResourceElement>()).Returns(x => new Uri(string.Format(_resourceUrlFormat, x.ArgAt<ResourceElement>(0).Id)));
            _urlResolver.ResolveRouteUrl(_unsupportedSearchParameters, _unsupportedSortingParameters).Returns(_selfUrl);

            ResourceElement observation1 = Samples.GetDefaultObservation().UpdateId("123");
            ResourceElement observation2 = Samples.GetDefaultObservation().UpdateId("abc");

            var resourceWrappers = new ResourceWrapper[]
            {
                CreateResourceWrapper(observation1),
                CreateResourceWrapper(observation2),
            };

            var searchResult = new SearchResult(resourceWrappers, _unsupportedSearchParameters, _unsupportedSortingParameters, continuationToken: null);

            ResourceElement actual = null;

            using (Mock.Property(() => Clock.UtcNowFunc, () => _dateTime))
            {
                actual = _bundleFactory.CreateSearchBundle(searchResult);
            }

            // Since there is no continuation token, there should not be next link.
            Assert.Null(actual.Scalar<string>("Bundle.link.where(relation='next').url"));
            Assert.Collection(
                actual.ToPoco<Bundle>().Entry,
                e => ValidateEntry(observation1.ToPoco<Observation>(), e),
                e => ValidateEntry(observation2.ToPoco<Observation>(), e));

            ResourceWrapper CreateResourceWrapper(ResourceElement resourceElement)
            {
                return new ResourceWrapper(
                    resourceElement,
                    new RawResource(_fhirJsonSerializer.SerializeToString(resourceElement.ToPoco<Observation>()), FhirResourceFormat.Json),
                    null,
                    false,
                    null,
                    null,
                    null);
            }

            void ValidateEntry(Observation expected, Bundle.EntryComponent actualEntry)
            {
                Assert.NotNull(actualEntry);
                Assert.NotNull(actualEntry.Resource);
                Assert.Equal(expected.Id, actualEntry.Resource.Id);
                Assert.Equal(string.Format(_resourceUrlFormat,  expected.Id), actualEntry.FullUrl);
                Assert.NotNull(actualEntry.Search);
                Assert.Equal(Bundle.SearchEntryMode.Match, actualEntry.Search.Mode);
            }
        }

        [Fact]
        public void GivenASearchResultWithContinuationToken_WhenCreateSearchBundle_ThenCorrectBundleShouldBeReturned()
        {
            string encodedContinuationToken = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(_continuationToken));
            _urlResolver.ResolveRouteUrl(_unsupportedSearchParameters, _unsupportedSortingParameters, encodedContinuationToken).Returns(_nextUrl);
            _urlResolver.ResolveRouteUrl(_unsupportedSearchParameters, _unsupportedSortingParameters).Returns(_selfUrl);

            var searchResult = new SearchResult(new ResourceWrapper[0], _unsupportedSearchParameters, _unsupportedSortingParameters, _continuationToken);

            ResourceElement actual = _bundleFactory.CreateSearchBundle(searchResult);

            Assert.Equal(_nextUrl.OriginalString, actual.Scalar<string>("Bundle.link.where(relation='next').url"));
        }
    }
}
