// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Api.Extensions;
using Microsoft.Health.Fhir.Shared.Tests;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Extensions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class HttpRequestExtensionsTests
    {
        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        [InlineData("_count=1&ct=foobarbaz+foobarbazCni1WdU46vh6NveXlWhkmAU6AjFNjaKgeFWBkqhwQ5WxkqewaHWRkpe7r6W5mZmpqZKwc6u1lZKLsFOFs5Blq4wfS4BobCmLZKOkpFiXnpqUpW1Uq5mSDblHSUchMrlKyU3NyUamtjAQAAAP//")]
        public async Task GivenAnUrlWithAContinuationToken_WhenEncoded_ThenCompareTheExpectedResults(string queryString)
        {
            IReadOnlyList<Tuple<string, string>> encodedUrlExpectedParameters = new Tuple<string, string>[]
            {
                Tuple.Create("_count", "1"),
                Tuple.Create("ct", "foobarbaz+foobarbazCni1WdU46vh6NveXlWhkmAU6AjFNjaKgeFWBkqhwQ5WxkqewaHWRkpe7r6W5mZmpqZKwc6u1lZKLsFOFs5Blq4wfS4BobCmLZKOkpFiXnpqUpW1Uq5mSDblHSUchMrlKyU3NyUamtjAQAAAP//"),
            };

            IReadOnlyList<Tuple<string, string>> notEncodedUrlExpectedParameters = new Tuple<string, string>[]
            {
                Tuple.Create("_count", "1"),
                Tuple.Create("ct", "foobarbaz foobarbazCni1WdU46vh6NveXlWhkmAU6AjFNjaKgeFWBkqhwQ5WxkqewaHWRkpe7r6W5mZmpqZKwc6u1lZKLsFOFs5Blq4wfS4BobCmLZKOkpFiXnpqUpW1Uq5mSDblHSUchMrlKyU3NyUamtjAQAAAP//"),
            };

            // Validate parameters for not encoded URI.
            HttpRequest rawRequest = await GetHttpRequestAsync(queryString);
            var rawRequestParameters = rawRequest.GetQueriesForSearch();
            Assert.Equal(notEncodedUrlExpectedParameters.Count, rawRequestParameters.Count);
            AssertQueryParameters(notEncodedUrlExpectedParameters, rawRequestParameters);

            // Check compatibility with previous query string parameter identification approach.
            var previousRequestParameters = GetPreviousQueriesForSearchApproach(rawRequest);
            Assert.Equal(notEncodedUrlExpectedParameters.Count, previousRequestParameters.Count);
            AssertQueryParameters(notEncodedUrlExpectedParameters, previousRequestParameters);

            // Validate parameters for encoded URI.
            string encodedQueryString = HttpFhirUtility.EncodeUrl(queryString);
            HttpRequest encodedRequest = await GetHttpRequestAsync(encodedQueryString);
            var encodedRequestParameters = encodedRequest.GetQueriesForSearch();
            Assert.Equal(encodedUrlExpectedParameters.Count, encodedRequestParameters.Count);
            AssertQueryParameters(encodedUrlExpectedParameters, encodedRequestParameters);
        }

        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        [InlineData(null, 0)]
        [InlineData("", 0)]
        [InlineData("foo", 1)]
        [InlineData("_has:HealthcareService:location:_id=00000000-0000-0000-0000-000000000000&name=foo&_count=4", 3)]
        [InlineData("_count=4&_sort=name&name=foo&_has:HealthcareService:location:_id=00000000-0000-0000-0000-000000000000", 4)]
        [InlineData("_count=4&_sort=name&name=BAR&name=foo&_has:HealthcareService:location:_id=00000000-0000-0000-0000-000000000000", 5)]
        [InlineData("_count=4&_sort=name&name=BAZ&_has:HealthcareService:location:_id=00000000-0000-0000-0000-000000000000", 4)]
        [InlineData("_lastUpdated=gt2022-09-20T18:35:41.6098088-07:00&_sort=-birthdate&_tag=00000000-0000-0000-0000-000000000000", 3)]
        [InlineData("name:missing=false&_has:PractitionerRole:service:practitioner=00000000-0000-0000-0000-000000000000&active:not=false&location:missing=false&_has:PractitionerRole:service:active=true", 5)]
        public async Task GivenAnUrl_WhenEncoded_ThenCompareTheResultingQueryParameters(string queryString, int expectedNumberOfParameters)
        {
            // Validate parameters for not encoded URI.
            HttpRequest rawRequest = await GetHttpRequestAsync(queryString);
            var rawRequestParameters = rawRequest.GetQueriesForSearch();
            Assert.Equal(expectedNumberOfParameters, rawRequestParameters.Count);

            // Check compatibility with previous query string parameter identification approach.
            var previousRequestParameters = GetPreviousQueriesForSearchApproach(rawRequest);
            Assert.Equal(expectedNumberOfParameters, previousRequestParameters.Count);

            // Validate parameters for encoded URI.
            string encodedQueryString = HttpFhirUtility.EncodeUrl(queryString);
            HttpRequest encodedRequest = await GetHttpRequestAsync(encodedQueryString);
            var encodedRequestParameters = encodedRequest.GetQueriesForSearch();
            Assert.Equal(expectedNumberOfParameters, encodedRequestParameters.Count);

            AssertQueryParameters(rawRequestParameters, encodedRequestParameters);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnUrl_WhenEncoded_ThenCompareTheExpectedResults()
        {
            const int expectedNumberOfParameters = 11;
            string queryString =
                "name:missing=false" +
                "&_has:PractitionerRole:service:practitioner=00000000-0000-0000-0000-000000000000" +
                "&active:not=false" +
                "&location:missing=false" +
                "&_has:PractitionerRole:service:active=true" +
                "&_has:SomeProperty:OtherProperty:Another1:not=false" +
                "&_has:SomeProperty:OtherProperty:Another2:not=true" +
                "&_revinclude=DiagnosticReport:result" +
                "&_include=MedicationRequest:*" +
                "&_include:iterate=Patient:*" +
                "&_lastUpdated1=2022-09-20T18:35:41.6098088-07:00";

            IReadOnlyList<Tuple<string, string>> expectedParameters = new Tuple<string, string>[]
            {
                Tuple.Create("name:missing", "false"),
                Tuple.Create("_has:PractitionerRole:service:practitioner", "00000000-0000-0000-0000-000000000000"),
                Tuple.Create("active:not", "false"),
                Tuple.Create("location:missing", "false"),
                Tuple.Create("_has:PractitionerRole:service:active", "true"),
                Tuple.Create("_has:SomeProperty:OtherProperty:Another1:not", "false"),
                Tuple.Create("_has:SomeProperty:OtherProperty:Another2:not", "true"),
                Tuple.Create("_revinclude", "DiagnosticReport:result"),
                Tuple.Create("_include", "MedicationRequest:*"),
                Tuple.Create("_include:iterate", "Patient:*"),
                Tuple.Create("_lastUpdated1", "2022-09-20T18:35:41.6098088-07:00"),
            };

            Assert.Equal(expectedNumberOfParameters, expectedParameters.Count);

            await AssertHttpRequestsWithQueryParameters(queryString, expectedParameters);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnUrlWithMultipleParameters_WhenEncoded_ThenCompareTheExpectedResults()
        {
            const int expectedNumberOfParameters = 4;
            string queryString = "_count=4&_sort=name&name=AB&_has:HealthcareService:location:_id=00000000-0000-0000-0000-000000000000";

            IReadOnlyList<Tuple<string, string>> expectedParameters = new Tuple<string, string>[]
            {
                Tuple.Create("_count", "4"),
                Tuple.Create("_sort", "name"),
                Tuple.Create("name", "AB"),
                Tuple.Create("_has:HealthcareService:location:_id", "00000000-0000-0000-0000-000000000000"),
            };

            Assert.Equal(expectedNumberOfParameters, expectedParameters.Count);

            await AssertHttpRequestsWithQueryParameters(queryString, expectedParameters);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnUrlWithDuplicatedParameters_WhenEncoded_ThenCompareTheExpectedResults()
        {
            const int expectedNumberOfParameters = 2;
            string queryString = "name=foo&name=bar";

            IReadOnlyList<Tuple<string, string>> expectedParameters = new Tuple<string, string>[]
            {
                Tuple.Create("name", "foo"),
                Tuple.Create("name", "bar"),
            };

            Assert.Equal(expectedNumberOfParameters, expectedParameters.Count);

            await AssertHttpRequestsWithQueryParameters(queryString, expectedParameters);
        }

        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        [InlineData("2022-09-20T18:35:41.6098088-07:00")]
        [InlineData("2022-09-21T20:34:37.0000000 00:00")]
        public async Task GivenAnUrlWithADatetime_WhenEncoded_ThenCompareTheExpectedResults(string value)
        {
            const int expectedNumberOfParameters = 1;

            IReadOnlyList<Tuple<string, string>> expectedParameters = new Tuple<string, string>[]
            {
                Tuple.Create("_datetime", value),
            };

            Assert.Equal(expectedNumberOfParameters, expectedParameters.Count);

            await AssertHttpRequestsWithQueryParameters($"_datetime={value}", expectedParameters);
        }

        private static async Task AssertHttpRequestsWithQueryParameters(string queryString, IReadOnlyList<Tuple<string, string>> expectedParameters)
        {
            // Validate parameters for not encoded URI.
            HttpRequest rawRequest = await GetHttpRequestAsync(queryString);
            var rawRequestParameters = rawRequest.GetQueriesForSearch();
            Assert.Equal(expectedParameters.Count, rawRequestParameters.Count);
            AssertQueryParameters(expectedParameters, rawRequestParameters);

            // Check compatibility with previous query string parameter identification approach.
            var previousRequestParameters = GetPreviousQueriesForSearchApproach(rawRequest);
            Assert.Equal(expectedParameters.Count, previousRequestParameters.Count);
            AssertQueryParameters(expectedParameters, previousRequestParameters);

            // Validate parameters for encoded URI.
            string encodedQueryString = HttpFhirUtility.EncodeUrl(queryString);
            HttpRequest encodedRequest = await GetHttpRequestAsync(encodedQueryString);
            var encodedRequestParameters = encodedRequest.GetQueriesForSearch();
            Assert.Equal(expectedParameters.Count, encodedRequestParameters.Count);
            AssertQueryParameters(expectedParameters, encodedRequestParameters);
        }

        private static void AssertQueryParameters(IReadOnlyList<Tuple<string, string>> expected, IReadOnlyList<Tuple<string, string>> actual)
        {
            Assert.NotNull(expected);

            Assert.NotNull(actual);

            Assert.Equal(expected.Count, actual.Count);

            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].Item1, actual[i].Item1);
                Assert.Equal(expected[i].Item2, actual[i].Item2);
            }
        }

        private static async Task<HttpRequest> GetHttpRequestAsync(string queryString)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Host = new HostString("localhost");
            httpContext.Request.ContentType = "application/json";
            if (!string.IsNullOrWhiteSpace(queryString))
            {
                httpContext.Request.QueryString = new QueryString(string.Concat("?", queryString));
            }

            using (var stream = new MemoryStream())
            using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync("{}");
                await writer.FlushAsync();
                stream.Position = 0;
                httpContext.Request.Body = stream;

                return httpContext.Request;
            }
        }

        /// <summary>
        /// This method runs the previous approach adopted to extract query string parameters from an URI.
        /// This method is only used to ensure the new approach using <see cref="HttpUtility"/> follows the expected behavior and does not cause regressions.
        /// </summary>
        private static IReadOnlyList<Tuple<string, string>> GetPreviousQueriesForSearchApproach(HttpRequest request)
        {
            IReadOnlyList<Tuple<string, string>> queries = Array.Empty<Tuple<string, string>>();

            if (request.Query != null)
            {
                queries = request.Query
                    .SelectMany(query => query.Value, (query, value) => Tuple.Create(query.Key, value))
                    .ToArray();
            }

            return queries;
        }
    }
}
