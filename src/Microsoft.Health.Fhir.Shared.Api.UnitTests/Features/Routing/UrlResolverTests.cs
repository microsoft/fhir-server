// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Models;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Routing
{
    public class UrlResolverTests
    {
        private const string Scheme = "http";
        private const string Host = "test";
        private const string ContinuationTokenQueryParamName = "ct";
        private const string DefaultRouteName = "Route";

        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        private readonly IUrlHelperFactory _urlHelperFactory = Substitute.For<IUrlHelperFactory>();
        private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        private readonly IActionContextAccessor _actionContextAccessor = Substitute.For<IActionContextAccessor>();

        private readonly IUrlHelper _urlHelper = Substitute.For<IUrlHelper>();
        private readonly DefaultHttpContext _httpContext = new DefaultHttpContext();
        private readonly ActionContext _actionContext = new ActionContext();

        private readonly UrlResolver _urlResolver;

        private UrlRouteContext _capturedUrlRouteContext;

        public UrlResolverTests()
        {
            _urlResolver = new UrlResolver(
                _fhirRequestContextAccessor,
                _urlHelperFactory,
                _httpContextAccessor,
                _actionContextAccessor);

            _fhirRequestContextAccessor.FhirRequestContext.RouteName = DefaultRouteName;

            _httpContextAccessor.HttpContext.Returns(_httpContext);

            _httpContext.Request.Scheme = Scheme;
            _httpContext.Request.Host = new HostString(Host);

            _actionContextAccessor.ActionContext.Returns(_actionContext);

            _urlHelper.RouteUrl(
                Arg.Do<UrlRouteContext>(c => _capturedUrlRouteContext = c));

            _urlHelperFactory.GetUrlHelper(_actionContext).Returns(_urlHelper);

            _urlHelper.RouteUrl(Arg.Any<UrlRouteContext>()).Returns($"{Scheme}://{Host}");
        }

        [Fact]
        public void GivenAResource_WhenResourceUrlIsResolved_ThenCorrectUrlShouldBeReturned()
        {
            const string id = "12345";

            ResourceElement patient = new Patient
            {
                Id = id,
            }.ToResourceElement();

            _urlResolver.ResolveResourceUrl(patient);

            ValidateUrlRouteContext(
                "ReadResource",
                routeValues =>
                {
                    Assert.Equal("Patient", routeValues[KnownActionParameterNames.ResourceType]);
                    Assert.Equal(id, routeValues[KnownActionParameterNames.Id]);
                });
        }

        [Fact]
        public void GivenAResource_WhenResourceUrlIsResolvedWithHistory_ThenCorrectUrlShouldBeReturned()
        {
            const string id = "12345";
            const string version = "abc";

            ResourceElement patient = new Patient
            {
                Id = id,
                VersionId = version,
            }.ToResourceElement();

            _urlResolver.ResolveResourceUrl(patient, includeVersion: true);

            ValidateUrlRouteContext(
                "ReadResourceWithVersionRoute",
                routeValues =>
                {
                    Assert.Equal("Patient", routeValues[KnownActionParameterNames.ResourceType]);
                    Assert.Equal(id, routeValues[KnownActionParameterNames.Id]);
                    Assert.Equal(version, routeValues[KnownActionParameterNames.Vid]);
                });
        }

        [Fact]
        public void GivenANullUnsupportedSearchParams_WhenSearchUrlIsResolved_ThenCorrectUrlShouldBeReturned()
        {
            _urlResolver.ResolveRouteUrl(unsupportedSearchParams: null, continuationToken: null);

            ValidateUrlRouteContext(
                routeValuesValidator: routeValues =>
                {
                    Assert.Empty(routeValues);
                });
        }

        [Fact]
        public void GivenAllSearchParamsAreSupported_WhenSearchUrlIsResolved_ThenCorrectUrlShouldBeReReturned()
        {
            string inputQueryString = "?param1=value1&param2=value2";
            Tuple<string, string>[] unsupportedSearchParams = null;
            Dictionary<string, object> expectedRouteValues = new Dictionary<string, object>()
            {
                { "param1", new StringValues("value1") },
                { "param2", new StringValues("value2") },
            };

            TestAndValidateRouteWithQueryParameter(inputQueryString, unsupportedSearchParams, null, expectedRouteValues);
        }

        [Fact]
        public void GivenASearchParamThatIsNotSupported_WhenSearchUrlIsResolved_ThenUnsupportedSearchParamShouldBeRemoved()
        {
            string inputQueryString = "?param1=value1&param2=value2";
            Tuple<string, string>[] unsupportedSearchParams = new[]
            {
                Tuple.Create("param2", "value2"),
            };
            Dictionary<string, object> expectedRouteValues = new Dictionary<string, object>()
            {
                { "param1", new StringValues("value1") },
            };

            TestAndValidateRouteWithQueryParameter(inputQueryString, unsupportedSearchParams, null, expectedRouteValues);
        }

        [Fact]
        public void GivenMultipleSearchParamsThatAreNotSupported_WhenSearchUrlIsResolved_ThenUnsupportedSearchParamShouldBeRemoved()
        {
            string inputQueryString = "?param1=value1&param1=value2&param2=value3&param2=value4&param3=value5";
            Tuple<string, string>[] unsupportedSearchParams = new[]
            {
                Tuple.Create("param2", "value3"),
                Tuple.Create("param2", "value4"),
            };
            Dictionary<string, object> expectedRouteValues = new Dictionary<string, object>()
            {
                { "param1", new StringValues(new[] { "value1", "value2" }) },
                { "param3", new StringValues("value5") },
            };

            TestAndValidateRouteWithQueryParameter(inputQueryString, unsupportedSearchParams, null, expectedRouteValues);
        }

        [Fact]
        public void GivenAContinuationToken_WhenSearchUrlIsResolved_ThenCorrectUrlShouldBeReturned()
        {
            string inputQueryString = "?param1=value1&param2=value2";
            Tuple<string, string>[] unsupportedSearchParams = null;
            string continuationToken = "continue";
            Dictionary<string, object> expectedRouteValues = new Dictionary<string, object>()
            {
                { "param1", new StringValues("value1") },
                { "param2", new StringValues("value2") },
                { ContinuationTokenQueryParamName, continuationToken },
            };

            TestAndValidateRouteWithQueryParameter(inputQueryString, unsupportedSearchParams, null, continuationToken, expectedRouteValues);
        }

        [Fact]
        public void GivenAQueryWithExistingContinuationToken_WhenSearchUrlIsResolvedWithANewContinuationToken_ThenContinuationTokenShouldBeUpdated()
        {
            string inputQueryString = $"?param1=value1&param2=value2&{ContinuationTokenQueryParamName}=abc";
            Tuple<string, string>[] unsupportedSearchParams = null;
            string continuationToken = "continue";
            Dictionary<string, object> expectedRouteValues = new Dictionary<string, object>()
            {
                { "param1", new StringValues("value1") },
                { "param2", new StringValues("value2") },
                { ContinuationTokenQueryParamName, continuationToken },
            };

            TestAndValidateRouteWithQueryParameter(inputQueryString, unsupportedSearchParams, null, continuationToken, expectedRouteValues);
        }

        [Fact]
        public void GivenAQueryWithExistingContinuationToken_WhenSearchUrlIsResolvedWithNoNewContinuationToken_ThenContinuationTokenShouldBeRemoved()
        {
            string inputQueryString = $"?param1=value1&{ContinuationTokenQueryParamName}=abc&param2=value2";
            Tuple<string, string>[] unsupportedSearchParams = null;
            string continuationToken = null;
            Dictionary<string, object> expectedRouteValues = new Dictionary<string, object>()
            {
                { "param1", new StringValues("value1") },
                { "param2", new StringValues("value2") },
            };

            TestAndValidateRouteWithQueryParameter(inputQueryString, unsupportedSearchParams, null, continuationToken, expectedRouteValues);
        }

        [Fact]
        public void GivenMultipleQueryParameterWithSameNameButOneHasInvalidValue_WhenSearchUrlIsResolved_ThenTheQueryParameterWithInvalidValueShouldBeRemoved()
        {
            string inputQueryString = $"?param1=value1&param2=123&param3=value3&param2=value2";
            Tuple<string, string>[] unsupportedSearchParams = new[]
            {
                Tuple.Create("param2", "123"),
            };
            string continuationToken = null;
            Dictionary<string, object> expectedRouteValues = new Dictionary<string, object>()
            {
                { "param1", new StringValues("value1") },
                { "param2", new StringValues("value2") },
                { "param3", new StringValues("value3") },
            };

            TestAndValidateRouteWithQueryParameter(inputQueryString, unsupportedSearchParams, null, continuationToken, expectedRouteValues);
        }

        [InlineData("?_sort=a,-b", null, "a,-b")]
        [InlineData("?_sort=a,-b", new[] { "a" }, "-b")]
        [InlineData("?_sort=a,-b", new[] { "b" }, "a")]
        [InlineData("?_sort=a", null, "a")]
        [InlineData("?_sort=a", new[] { "b" }, "a")]
        [InlineData("?_sort=a", new[] { "a" })]
        [InlineData("?_sort=a,-b&_sort=-c,d", new[] { "b" }, "a", "-c,d")]
        [InlineData("?_sort=a,-b&_sort=-c,d", new[] { "b", "c", "d" }, "a")]
        [InlineData("?_sort=a,-b&_sort=-c,d", new[] { "a", "b", "c", "d" })]
        [InlineData("?_sort=a,b,c", new[] { "a" }, "b,c")]
        [InlineData("?_sort=", null)]
        [InlineData("?_sort=", new[] { "a" })]
        [Theory]
        public void GivenSortingParameters_WhenSearchUrlIsResolved_ThenCorrectUrlShouldBeReReturned(string queryString, string[] unsupportedSortingValues, params string[] expectedValues)
        {
            TestAndValidateRouteWithQueryParameter(
                queryString,
                Array.Empty<Tuple<string, string>>(),
                unsupportedSortingValues?.Select(v => (v, "no reason")).ToList(),
                expectedValues?.Length > 0 ? new Dictionary<string, object> { { KnownQueryParameterNames.Sort, new StringValues(expectedValues) } } : new Dictionary<string, object>());
        }

        [Fact]
        public void GivenAnExportOperation_WhenOperationResultUrlIsResolved_ThenCorrectUrlShouldBeReturned()
        {
            const string id = "12345";
            const string opName = OperationsConstants.Export;

            _urlResolver.ResolveOperationResultUrl(opName, id);

            ValidateUrlRouteContext(
                RouteNames.GetExportStatusById,
                routeValues =>
                {
                    Assert.Equal(id, routeValues[KnownActionParameterNames.Id]);
                });
        }

        [Fact]
        public void GivenANonExportOperation_WhenOperationResultUrlIsResolved_ThenOperationNotImplementedExceptionShouldBeThrown()
        {
            const string id = "12345";
            const string opName = "import";

            Assert.Throws<OperationNotImplementedException>(() => _urlResolver.ResolveOperationResultUrl(opName, id));
        }

        private void TestAndValidateRouteWithQueryParameter(
            string inputQueryString,
            Tuple<string, string>[] unsupportedSearchParams,
            IReadOnlyList<(string parameterName, string reason)> unsupportedSortingParameters,
            Dictionary<string, object> expectedRouteValues)
        {
            TestAndValidateRouteWithQueryParameter(
                inputQueryString,
                unsupportedSearchParams,
                unsupportedSortingParameters,
                null,
                expectedRouteValues);
        }

        private void TestAndValidateRouteWithQueryParameter(
            string inputQueryString,
            Tuple<string, string>[] unsupportedSearchParams,
            IReadOnlyList<(string parameterName, string reason)> unsupportedSortingParameters,
            string continuationToken,
            Dictionary<string, object> expectedRouteValues)
        {
            _httpContext.Request.QueryString = new QueryString(inputQueryString);

            _urlResolver.ResolveRouteUrl(unsupportedSearchParams, unsupportedSortingParameters, continuationToken);

            ValidateUrlRouteContext(
                routeValuesValidator: routeValues =>
                {
                    foreach (KeyValuePair<string, object> expectedRouteValue in expectedRouteValues)
                    {
                        Assert.True(routeValues.ContainsKey(expectedRouteValue.Key));

                        object expectedValue = expectedRouteValue.Value;
                        object actualValue = routeValues[expectedRouteValue.Key];

                        Assert.IsType(expectedValue.GetType(), actualValue);

                        Assert.Equal(expectedValue, actualValue);
                    }
                });
        }

        private void ValidateUrlRouteContext(string routeName = DefaultRouteName, Action<RouteValueDictionary> routeValuesValidator = null)
        {
            Assert.NotNull(_capturedUrlRouteContext);
            Assert.Equal(routeName, _capturedUrlRouteContext.RouteName);
            Assert.Equal(Scheme, _capturedUrlRouteContext.Protocol);
            Assert.Equal(Host, _capturedUrlRouteContext.Host);

            RouteValueDictionary routeValues = Assert.IsType<RouteValueDictionary>(_capturedUrlRouteContext.Values);

            routeValuesValidator(routeValues);
        }
    }
}
