// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using SqlSearchDebugger;
using SqlSearchDebugger.Mocks;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlSearchDebuggerTests
    {
        [Fact]
        public void GivenDifferentInputOrders_WhenModelIsCreated_ThenIdsAreStable()
        {
            Uri firstSearchParameter = new("http://example.org/SearchParameter/a");
            Uri secondSearchParameter = new("http://example.org/SearchParameter/z");
            var first = new FakeSqlServerFhirModel(
                ["Patient", "Observation"],
                [secondSearchParameter, firstSearchParameter]);
            var second = new FakeSqlServerFhirModel(
                ["Observation", "Patient"],
                [firstSearchParameter, secondSearchParameter]);

            Assert.Equal(
                new[] { first.GetResourceTypeId("Observation"), first.GetResourceTypeId("Patient") },
                new[] { second.GetResourceTypeId("Observation"), second.GetResourceTypeId("Patient") });
            Assert.Equal(
                new[] { first.GetSearchParamId(firstSearchParameter), first.GetSearchParamId(secondSearchParameter) },
                new[] { second.GetSearchParamId(firstSearchParameter), second.GetSearchParamId(secondSearchParameter) });
        }

        [Fact]
        public void GivenResourceTypes_WhenModelIsCreated_ThenTheyAreImmediatelyAvailable()
        {
            var model = new FakeSqlServerFhirModel(["Patient", "Observation"], []);

            Assert.True(model.TryGetResourceTypeId("Patient", out _));
            Assert.True(model.TryGetResourceTypeId("Observation", out _));
            Assert.Equal(2, model.GetAllResourceTypes().Count);
            Assert.False(model.TryGetResourceTypeId("NotAResource", out _));
            Assert.Equal(2, model.ResourceTypeCount);
        }

        [Fact]
        public void GivenConcurrentRequests_WhenIdsAreRead_ThenResultsAreStable()
        {
            var model = new FakeSqlServerFhirModel(
                ["Patient", "Observation"],
                [new Uri("http://example.org/SearchParameter/name")]);
            short[] resourceTypeIds = new short[1000];
            short[] searchParameterIds = new short[1000];
            int[] systemIds = new int[1000];
            int[] quantityCodeIds = new int[1000];
            var searchParameterUri = new Uri("http://example.org/SearchParameter/name");

            Parallel.For(0, systemIds.Length, index =>
            {
                resourceTypeIds[index] = model.GetResourceTypeId("Patient");
                searchParameterIds[index] = model.GetSearchParamId(searchParameterUri);
                systemIds[index] = model.GetSystemId(index % 2 == 0 ? "HTTP://EXAMPLE.ORG" : "http://example.org");
                quantityCodeIds[index] = model.GetQuantityCodeId(index % 2 == 0 ? "MG" : "mg");
            });

            Assert.Single(resourceTypeIds.Distinct());
            Assert.Single(searchParameterIds.Distinct());
            Assert.Single(systemIds.Distinct());
            Assert.Single(quantityCodeIds.Distinct());
        }

        [Fact]
        public void GivenEncodedServerToken_WhenParsed_ThenTokenIsDecoded()
        {
            string encoded = ContinuationTokenEncoder.Encode("[123]");

            ContinuationToken token = ParserHelpers.ParseContinuationToken(encoded);

            Assert.Equal("[123]", token.ToString());
        }

        [Fact]
        public void GivenRawInternalToken_WhenParsed_ThenTokenIsAccepted()
        {
            ContinuationToken token = ParserHelpers.ParseContinuationToken("[123]");

            Assert.Equal("[123]", token.ToString());
        }

        [Theory]
        [InlineData("not-a-token")]
        [InlineData("YWJj")]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("[null]")]
        [InlineData("[\"abc\"]")]
        [InlineData("[1,\"abc\"]")]
        [InlineData("[1,2,3,4]")]
        public void GivenInvalidServerToken_WhenParsed_ThenExceptionIsThrown(string token)
        {
            Assert.Throws<BadRequestException>(() => ParserHelpers.ParseContinuationToken(token));
        }
    }
}
