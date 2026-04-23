// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Conformance
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Conformance)]
    public class USCoreInstantiateCapabilityTests
    {
        private const string RawResourceFormat =
            @"{{
                  ""resourceType"" : ""StructureDefinition"",
                  ""id"" : ""{0}"",
                  ""url"" : ""http://hl7.org/fhir/us/core/StructureDefinition/{0}"",
                  ""version"" : ""{1}"",
                  ""name"" : ""Name"",
                  ""kind"" : ""resource"",
                  ""abstract"" : false,
                  ""type"" : ""Type""
            }}";

        private static readonly FhirJsonParser Parser = new FhirJsonParser();
        private readonly USCoreInstantiateCapability _capability;
        private readonly ISearchService _searchService;
        private readonly IResourceDeserializer _resourceDeserializer;

        public USCoreInstantiateCapabilityTests()
        {
            _searchService = Substitute.For<ISearchService>();
            _resourceDeserializer = new ResourceDeserializer(
                (FhirResourceFormat.Json, new Func<string, string, DateTimeOffset, ResourceElement>((data, version, lastUpdated) =>
                {
                    return Parser.Parse(data).ToResourceElement();
                })));

            var scoped = Substitute.For<IScoped<ISearchService>>();
            scoped.Value.Returns(_searchService);

            var factory = Substitute.For<Func<IScoped<ISearchService>>>();
            factory.Invoke().Returns(scoped);

            _capability = new USCoreInstantiateCapability(
                factory,
                _resourceDeserializer,
                Substitute.For<ILogger<USCoreInstantiateCapability>>());
        }

        [Theory]
        [MemberData(nameof(GetVersionsData))]
        public async Task GivenSearchRequest_WhenUSCoreProfilesAreFound_ThenCapabilityShouldReturnsCorrectUrls(
            string[] versions)
        {
            var result = CreateSearchResult(versions);
            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>(),
                Arg.Any<ResourceVersionType>(),
                Arg.Any<bool>(),
                Arg.Any<bool>())
                .Returns(result);

            var resourceType = default(string);
            var parameters = default(IReadOnlyList<Tuple<string, string>>);
            _searchService
                .When(x => x.SearchAsync(
                    Arg.Any<string>(),
                    Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<bool>(),
                    Arg.Any<ResourceVersionType>(),
                    Arg.Any<bool>(),
                    Arg.Any<bool>()))
                .Do(x =>
                {
                    resourceType = x.Arg<string>();
                    parameters = x.Arg<IReadOnlyList<Tuple<string, string>>>();
                });

            var actual = await _capability.GetCanonicalUrlsAsync(CancellationToken.None);
            var expected = GetExpectedUrls(versions);
            Assert.True(expected.SetEquals(actual));

            var p = parameters?.ToDictionary(y => y.Item1, y => y.Item2) ?? new Dictionary<string, string>();
            Assert.True(p.TryGetValue("url:below", out var v0) && string.Equals(v0, USCoreInstantiateCapability.UrlPrefix, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(KnownResourceTypes.StructureDefinition, resourceType);
        }

        [Fact]
        public async Task GivenSearchRequest_WhenResultsDoNotFitInOnePage_ThenCapabilityShouldReturnsCorrectUrls()
        {
            var versions = Enumerable.Range(1, 10).Select(x => string.Format("{0}.0.0", x)).ToArray();
            var result1 = CreateSearchResult(versions.Take(4).ToArray(), Guid.NewGuid().ToString());
            var result2 = CreateSearchResult(versions.Skip(4).Take(4).ToArray(), Guid.NewGuid().ToString());
            var result3 = CreateSearchResult(versions.Skip(8).Take(2).ToArray());
            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>(),
                Arg.Any<ResourceVersionType>(),
                Arg.Any<bool>(),
                Arg.Any<bool>())
                .Returns(result1, result2, result3);

            var actual = await _capability.GetCanonicalUrlsAsync(CancellationToken.None);
            var expected = GetExpectedUrls(versions);
            Assert.True(expected.SetEquals(actual));

            await _searchService.Received(3).SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>(),
                Arg.Any<ResourceVersionType>(),
                Arg.Any<bool>(),
                Arg.Any<bool>());
        }

        [Fact]
        public async Task GivenSearchRequest_WhenSearchFails_ThenCapabilityShouldRethrowException()
        {
            _searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>(),
                Arg.Any<ResourceVersionType>(),
                Arg.Any<bool>(),
                Arg.Any<bool>())
                .Throws(new Exception("failed"));
            await Assert.ThrowsAnyAsync<Exception>(() => _capability.GetCanonicalUrlsAsync(CancellationToken.None));
        }

        private static SearchResult CreateSearchResult(
            string[] versions,
            string continuationToken = null)
        {
            var entries = new List<SearchResultEntry>();
            foreach (var version in versions)
            {
                var resource = new ResourceWrapper(
                    Guid.NewGuid().ToString(),
                    version,
                    KnownResourceTypes.StructureDefinition,
                    new RawResource(
                        string.Format(RawResourceFormat, Guid.NewGuid().ToString(), version),
                        FhirResourceFormat.Json,
                        false),
                    null,
                    DateTimeOffset.UtcNow,
                    false,
                    null,
                    null,
                    null);
                entries.Add(new SearchResultEntry(resource));
            }

            return new SearchResult(
                entries,
                continuationToken,
                null,
                new List<Tuple<string, string>>());
        }

        private HashSet<string> GetExpectedUrls(string[] versions)
        {
            var urls = new HashSet<string>();
            foreach (var version in versions.Distinct())
            {
                var v = string.IsNullOrEmpty(version) ? USCoreInstantiateCapability.UnknownVersion : version;
                urls.Add($"{USCoreInstantiateCapability.BaseUrl}|{v}");
            }

            return urls;
        }

        public static IEnumerable<object[]> GetVersionsData()
        {
            var data = new[]
            {
                new object[]
                {
                    new string[] { },
                },
                new object[]
                {
                    new string[] { "6.1.0" },
                },
                new object[]
                {
                    new string[]
                    {
                        "6.0.0",
                        "6.1.0",
                        "7.0.0",
                    },
                },
                new object[]
                {
                    new string[]
                    {
                        "6.1.0",
                        "6.1.0",
                        "7.0.0",
                        "7.0.0",
                        "8.0.0",
                    },
                },
                new object[]
                {
                    new string[]
                    {
                        null,
                        "6.1.0",
                        string.Empty,
                        "6.1.0",
                        "7.0.0",
                        "7.0.0",
                        null,
                        "8.0.0",
                        null,
                    },
                },
            };

            foreach (var d in data)
            {
                yield return d;
            }
        }
    }
}
