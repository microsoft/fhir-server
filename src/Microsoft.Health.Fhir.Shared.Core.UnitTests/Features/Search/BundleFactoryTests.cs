// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class BundleFactoryTests
    {
        private readonly FhirJsonSerializer _fhirJsonSerializer = new FhirJsonSerializer();
        private readonly IUrlResolver _urlResolver = Substitute.For<IUrlResolver>();
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        private readonly BundleFactory _bundleFactory;

        private const string _continuationToken = "ct";
        private const string _resourceUrlFormat = "http://resource/{0}";
        private static readonly string _correlationId = Guid.NewGuid().ToString();
        private static readonly Uri _selfUrl = new Uri("http://self/");
        private static readonly Uri _nextUrl = new Uri("http://next/");
        private static readonly IReadOnlyList<Tuple<string, string>> _unsupportedSearchParameters = new Tuple<string, string>[0];

        private static readonly DateTimeOffset _dateTime = new DateTimeOffset(2019, 1, 5, 15, 30, 23, TimeSpan.FromHours(8));

        public BundleFactoryTests()
        {
            _bundleFactory = new BundleFactory(
                _urlResolver,
                _fhirRequestContextAccessor,
                NullLogger<BundleFactory>.Instance);

            IFhirRequestContext fhirRequestContext = Substitute.For<IFhirRequestContext>();

            fhirRequestContext.CorrelationId.Returns(_correlationId);

            _fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);
        }

#if NET8_0_OR_GREATER
        [Fact]
        public void GivenAnEmptySearchResult_WhenCreateSearchBundle_ThenCorrectBundleShouldBeReturned()
        {
            _urlResolver.ResolveRouteUrl(_unsupportedSearchParameters).Returns(_selfUrl);

            ResourceElement actual = null;

            using (Mock.Property(() => ClockResolver.TimeProvider, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(_dateTime)))
            {
                actual = _bundleFactory.CreateSearchBundle(new SearchResult(new SearchResultEntry[0], null, null, _unsupportedSearchParameters));
            }

            Assert.NotNull(actual);
            Assert.Equal(Bundle.BundleType.Searchset.ToString().ToLowerInvariant(), actual.Scalar<string>("Bundle.type"));
            Assert.Equal(_correlationId.ToString(), actual.Id);
            Assert.Equal(_dateTime, actual.LastUpdated);
            Assert.Equal(_selfUrl.OriginalString, actual.Scalar<string>("Bundle.link.where(relation='self').url"));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void GivenASearchResult_WhenCreateSearchBundle_ThenCorrectBundleShouldBeReturned(bool pretty)
        {
            _urlResolver.ResolveResourceWrapperUrl(Arg.Any<ResourceWrapper>()).Returns(x => new Uri(string.Format(_resourceUrlFormat, x.ArgAt<ResourceWrapper>(0).ResourceId)));
            _urlResolver.ResolveRouteUrl(_unsupportedSearchParameters).Returns(_selfUrl);

            ResourceElement observation1 = Samples.GetDefaultObservation().UpdateId("123");
            ResourceElement observation2 = Samples.GetDefaultObservation().UpdateId("abc");

            var resourceWrappers = new SearchResultEntry[]
            {
                new SearchResultEntry(CreateResourceWrapper(observation1, HttpMethod.Post)),
                new SearchResultEntry(CreateResourceWrapper(observation2, HttpMethod.Post)),
            };

            var searchResult = new SearchResult(resourceWrappers, continuationToken: null, sortOrder: null, unsupportedSearchParameters: _unsupportedSearchParameters);

            ResourceElement actual = null;

            using (Mock.Property(() => ClockResolver.TimeProvider, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(_dateTime)))
            {
                actual = _bundleFactory.CreateSearchBundle(searchResult);
            }

            // Since there is no continuation token, there should not be next link.
            Assert.Null(actual.Scalar<string>("Bundle.link.where(relation='next').url"));
            Assert.Collection(
                actual.ToPoco<Bundle>().Entry,
                async e => await ValidateEntry(observation1.ToPoco<Observation>(), e),
                async e => await ValidateEntry(observation2.ToPoco<Observation>(), e));

            async Task ValidateEntry(Observation expected, Bundle.EntryComponent actualEntry)
            {
                Assert.NotNull(actualEntry);

                var raw = actualEntry as RawBundleEntryComponent;

                Assert.NotNull(raw);
                Assert.NotNull(raw.ResourceElement);

                using (var ms = new MemoryStream())
                using (var sr = new StreamReader(ms))
                {
                    await raw.ResourceElement.SerializeToStreamAsUtf8Json(ms, pretty);
                    ms.Seek(0, SeekOrigin.Begin);
                    var resourceData = await sr.ReadToEndAsync();
                    Assert.NotNull(resourceData);

                    Resource resource;
                    resource = new FhirJsonParser().Parse(resourceData) as Resource;

                    Assert.Equal(expected.Id, resource.Id);
                    Assert.Equal(string.Format(_resourceUrlFormat, expected.Id), raw.FullUrl);
                    Assert.NotNull(raw.Search);
                    Assert.Equal(Bundle.SearchEntryMode.Match, raw.Search.Mode);
                }
            }
        }
#endif

        private ResourceWrapper CreateResourceWrapper(ResourceElement resourceElement, HttpMethod httpMethod)
        {
            return new ResourceWrapper(
                resourceElement,
                new RawResource(_fhirJsonSerializer.SerializeToString(resourceElement.ToPoco<Observation>()), FhirResourceFormat.Json, isMetaSet: false),
                new ResourceRequest(httpMethod, url: "http://test/Resource/resourceId"),
                false,
                null,
                null,
                null);
        }

        [Fact]
        public void GivenASearchResultWithContinuationToken_WhenCreateSearchBundle_ThenCorrectBundleShouldBeReturned()
        {
            string encodedContinuationToken = ContinuationTokenConverter.Encode(_continuationToken);
            _urlResolver.ResolveRouteUrl(_unsupportedSearchParameters, null, encodedContinuationToken, true).Returns(_nextUrl);
            _urlResolver.ResolveRouteUrl(_unsupportedSearchParameters).Returns(_selfUrl);

            var searchResult = new SearchResult(new SearchResultEntry[0], _continuationToken, null, _unsupportedSearchParameters);

            ResourceElement actual = _bundleFactory.CreateSearchBundle(searchResult);

            Assert.Equal(_nextUrl.OriginalString, actual.Scalar<string>("Bundle.link.where(relation='next').url"));
        }

        [Theory]
        [InlineData("123", "1", "POST", "201 Created")]
        [InlineData("123", "1", "PUT", "201 Created")]
        [InlineData("123", "2", "PUT", "200 OK")]
        [InlineData("123", "2", "PATCH", "200 OK")]
        [InlineData("123", "2", "DELETE", "204 NoContent")]
        public void GivenAHistoryResultWithDifferentStatuses_WhenCreateHistoryBundle_ThenCorrectBundleShouldBeReturned(string id, string version, string method, string statusString)
        {
            _urlResolver.ResolveResourceWrapperUrl(Arg.Any<ResourceWrapper>()).Returns(x => new Uri(string.Format(_resourceUrlFormat, x.ArgAt<ResourceWrapper>(0).ResourceId)));
            _urlResolver.ResolveRouteUrl(_unsupportedSearchParameters).Returns(_selfUrl);

            _urlResolver.ResolveResourceWrapperUrl(Arg.Any<ResourceWrapper>(), Arg.Any<bool>()).Returns(x => new Uri(string.Format(_resourceUrlFormat, x.ArgAt<ResourceWrapper>(0).ResourceId)));

            ResourceElement observation1 = Samples.GetDefaultObservation().UpdateId(id).UpdateVersion(version);

            var resourceWrappers = new[]
            {
                new SearchResultEntry(CreateResourceWrapper(observation1, new HttpMethod(method))),
            };

            var searchResult = new SearchResult(resourceWrappers, continuationToken: null, sortOrder: null, unsupportedSearchParameters: _unsupportedSearchParameters);

            var actual = _bundleFactory.CreateHistoryBundle(searchResult);

            Assert.NotNull(actual.ToPoco<Bundle>().Entry[0].Request.Method);
            Assert.NotNull(actual.ToPoco<Bundle>().Entry[0].Request.Url);
            Assert.Equal(statusString, actual.ToPoco<Bundle>().Entry[0].Response.Status);
        }

        [Fact]
        public void GivenAHistoryResultWithAllHttpVerbs_WhenCreateHistoryBundle_ThenBundleShouldNotCrash()
        {
            _urlResolver.ResolveRouteUrl(_unsupportedSearchParameters).Returns(_selfUrl);
            _urlResolver.ResolveResourceWrapperUrl(Arg.Any<ResourceWrapper>(), Arg.Any<bool>()).Returns(x => new Uri(string.Format(_resourceUrlFormat, x.ArgAt<ResourceWrapper>(0).ResourceId)));

            foreach (var verb in Enum.GetValues<Bundle.HTTPVerb>())
            {
                ResourceElement observation1 = Samples.GetDefaultObservation().UpdateId("123").UpdateVersion("1");

                var resourceWrappers = new[]
                {
                    new SearchResultEntry(CreateResourceWrapper(observation1, new HttpMethod(verb.ToString()))),
                };

                var searchResult = new SearchResult(resourceWrappers, continuationToken: null, sortOrder: null, unsupportedSearchParameters: _unsupportedSearchParameters);

                var actual = _bundleFactory.CreateHistoryBundle(searchResult);
                Assert.NotNull(actual.ToPoco<Bundle>().Entry[0].Response.Status);
            }
        }
    }
}
