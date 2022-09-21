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
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Extensions
{
    [Trait(Traits.Category, Categories.Search)]
    public class HttpRequestExtensionsTests
    {
        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        [InlineData(null, 0)]
        [InlineData("", 0)]
        [InlineData("foo", 1)]
        [InlineData("_has:HealthcareService:location:_id=00000000-0000-0000-0000-000000000000&name=foo&_count=4", 3)]
        [InlineData("_count=4&_sort=name&name=foo&_has:HealthcareService:location:_id=00000000-0000-0000-0000-000000000000", 4)]
        [InlineData("_count=4&_sort=name&name=BAR&name=foo&_has:HealthcareService:location:_id=00000000-0000-0000-0000-000000000000", 5)]
        [InlineData("_count=4&_sort=name&name=BAZ&_has%3AHealthcareService%3Alocation%3A_id=00000000-0000-0000-0000-000000000000", 4)]
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
            string encodedQueryString = HttpUtility.UrlEncode(queryString);
            HttpRequest encodedRequest = await GetHttpRequestAsync(encodedQueryString);
            var encodedRequestParameters = encodedRequest.GetQueriesForSearch();
            Assert.Equal(expectedNumberOfParameters, encodedRequestParameters.Count);

            AssertQueryParameters(rawRequestParameters, encodedRequestParameters);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnUrl_WhenEncoded_ThenCompareTheExpectedResults()
        {
            const int expectedNumberOfParameters = 7;
            string queryString = "name:missing=false&_has:PractitionerRole:service:practitioner=00000000-0000-0000-0000-000000000000&active:not=false&location:missing=false&_has:PractitionerRole:service:active=true&_has:SomeProperty:OtherProperty:Another1:not=false&_has:SomeProperty:OtherProperty:Another2:not=true";

            IReadOnlyList<Tuple<string, string>> expectedParameters = new Tuple<string, string>[]
            {
                Tuple.Create("name:missing", "false"),
                Tuple.Create("_has:PractitionerRole:service:practitioner", "00000000-0000-0000-0000-000000000000"),
                Tuple.Create("active:not", "false"),
                Tuple.Create("location:missing", "false"),
                Tuple.Create("_has:PractitionerRole:service:active", "true"),
                Tuple.Create("_has:SomeProperty:OtherProperty:Another1:not", "false"),
                Tuple.Create("_has:SomeProperty:OtherProperty:Another2:not", "true"),
            };

            Assert.Equal(expectedNumberOfParameters, expectedParameters.Count);

            // Validate parameters for not encoded URI.
            HttpRequest rawRequest = await GetHttpRequestAsync(queryString);
            var rawRequestParameters = rawRequest.GetQueriesForSearch();
            Assert.Equal(expectedNumberOfParameters, rawRequestParameters.Count);
            AssertQueryParameters(expectedParameters, rawRequestParameters);

            // Check compatibility with previous query string parameter identification approach.
            var previousRequestParameters = GetPreviousQueriesForSearchApproach(rawRequest);
            Assert.Equal(expectedNumberOfParameters, previousRequestParameters.Count);
            AssertQueryParameters(expectedParameters, previousRequestParameters);

            // Validate parameters for encoded URI.
            string encodedQueryString = HttpUtility.UrlEncode(queryString);
            HttpRequest encodedRequest = await GetHttpRequestAsync(encodedQueryString);
            var encodedRequestParameters = encodedRequest.GetQueriesForSearch();
            Assert.Equal(expectedNumberOfParameters, encodedRequestParameters.Count);
            AssertQueryParameters(expectedParameters, encodedRequestParameters);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAnUrlWithDuplicatedParameters_WhenEncoded_ThenCompareTheExpectedResults()
        {
            const int expectedNumberOfParameters = 2;
            string queryString = "name:missing=false&name:missing=true";

            IReadOnlyList<Tuple<string, string>> expectedParameters = new Tuple<string, string>[]
            {
                Tuple.Create("name:missing", "false"),
                Tuple.Create("name:missing", "true"),
            };

            Assert.Equal(expectedNumberOfParameters, expectedParameters.Count);

            // Validate parameters for not encoded URI.
            HttpRequest rawRequest = await GetHttpRequestAsync(queryString);
            var rawRequestParameters = rawRequest.GetQueriesForSearch();
            Assert.Equal(expectedNumberOfParameters, rawRequestParameters.Count);
            AssertQueryParameters(expectedParameters, rawRequestParameters);

            // Check compatibility with previous query string parameter identification approach.
            var previousRequestParameters = GetPreviousQueriesForSearchApproach(rawRequest);
            Assert.Equal(expectedNumberOfParameters, previousRequestParameters.Count);
            AssertQueryParameters(expectedParameters, previousRequestParameters);

            // Validate parameters for encoded URI.
            string encodedQueryString = HttpUtility.UrlEncode(queryString);
            HttpRequest encodedRequest = await GetHttpRequestAsync(encodedQueryString);
            var encodedRequestParameters = encodedRequest.GetQueriesForSearch();
            Assert.Equal(expectedNumberOfParameters, encodedRequestParameters.Count);
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
            httpContext.Request.Method = "POST";
            httpContext.Request.Scheme = "http";
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
        /// This methods runs the previous approach adopted to extract query string parameters from an URI.
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
