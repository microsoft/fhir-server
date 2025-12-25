// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Conformance
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Conformance)]
    public class USCore6InstantiateCapabilityTests
    {
        private const string Url = "http://hl7.org/fhir/us/core/CapabilityStatement/us-core-server";
        private const string UrlPrefix = "http://hl7.org/fhir/us/core/StructureDefinition/";
        private const string Version = "6.0.0";

        private readonly USCore6InstantiateCapability _capability;
        private readonly ISearchService _searchService;

        public USCore6InstantiateCapabilityTests()
        {
            _searchService = Substitute.For<ISearchService>();

            var scoped = Substitute.For<IScoped<ISearchService>>();
            scoped.Value.Returns(_searchService);

            var factory = Substitute.For<Func<IScoped<ISearchService>>>();
            factory.Invoke().Returns(scoped);

            _capability = new USCore6InstantiateCapability(
                factory,
                Substitute.For<ILogger<USCore6InstantiateCapability>>());
        }

        [Theory]
        [InlineData(10)]
        [InlineData(1)]
        [InlineData(0)]
        public async Task GivenSearchRequest_WhenUSCore6ProfilesAreFound_ThenCapabilityShouldReturnsCorrectUrls(
            int count)
        {
            var result = new SearchResult(
                count,
                new List<Tuple<string, string>>());
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

            var urls = await _capability.GetCanonicalUrlsAsync(CancellationToken.None);

            Assert.Equal(count > 0, urls?.Any());
            if (count > 0)
            {
                Assert.Equal(Url, urls.FirstOrDefault(), StringComparer.OrdinalIgnoreCase);
            }

            Assert.Equal(KnownResourceTypes.StructureDefinition, resourceType, StringComparer.OrdinalIgnoreCase);

            var p = parameters?.ToDictionary(y => y.Item1, y => y.Item2) ?? new Dictionary<string, string>();
            Assert.True(
                p.TryGetValue("url:below", out var v0) && string.Equals(v0, UrlPrefix, StringComparison.OrdinalIgnoreCase)
                && p.TryGetValue("version", out var v1) && string.Equals(v1, Version, StringComparison.OrdinalIgnoreCase)
                && p.TryGetValue("_summary", out var v2) && string.Equals(v2, "count", StringComparison.OrdinalIgnoreCase));
        }
    }
}
