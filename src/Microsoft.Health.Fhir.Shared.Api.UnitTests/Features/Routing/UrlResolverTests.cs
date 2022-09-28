// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Features.Bundle;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Routing
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class UrlResolverTests
    {
        private const string Scheme = "http";
        private const string Host = "test";
        private const string ContinuationTokenQueryParamName = "ct";
        private const string DefaultRouteName = "Route";

        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        private readonly IUrlHelperFactory _urlHelperFactory = Substitute.For<IUrlHelperFactory>();
        private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        private readonly IActionContextAccessor _actionContextAccessor = Substitute.For<IActionContextAccessor>();
        private readonly IBundleHttpContextAccessor _bundleHttpContextAccessor = Substitute.For<IBundleHttpContextAccessor>();

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
                _actionContextAccessor,
                _bundleHttpContextAccessor);

            _fhirRequestContextAccessor.RequestContext.RouteName = DefaultRouteName;

            _httpContextAccessor.HttpContext.Returns(_httpContext);

            _httpContext.Request.Scheme = Scheme;
            _httpContext.Request.Host = new HostString(Host);

            _actionContextAccessor.ActionContext.Returns(_actionContext);

            _urlHelper.RouteUrl(
                Arg.Do<UrlRouteContext>(c => _capturedUrlRouteContext = c));

            _urlHelperFactory.GetUrlHelper(_actionContext).Returns(_urlHelper);

            _urlHelper.RouteUrl(Arg.Any<UrlRouteContext>()).Returns($"{Scheme}://{Host}");

            _bundleHttpContextAccessor.HttpContext.Returns((HttpContext)null);
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
            _urlResolver.ResolveRouteUrl(unsupportedSearchParams: null, null, continuationToken: null);

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

            TestAndValidateRouteWithQueryParameter(inputQueryString, null, unsupportedSearchParams, null, expectedRouteValues);
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

            TestAndValidateRouteWithQueryParameter(inputQueryString, null, unsupportedSearchParams, null, expectedRouteValues);
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

            TestAndValidateRouteWithQueryParameter(inputQueryString, null, unsupportedSearchParams, null, expectedRouteValues);
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

            TestAndValidateRouteWithQueryParameter(inputQueryString, null, unsupportedSearchParams, continuationToken, expectedRouteValues);
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

            TestAndValidateRouteWithQueryParameter(inputQueryString, null, unsupportedSearchParams, continuationToken, expectedRouteValues);
        }

        [Fact]
        public void GivenAQueryWithExistingContinuationToken_WhenSearchUrlIsResolvedWithNoNewContinuationToken_ThenContinuationTokenShouldBeRetained()
        {
            string inputQueryString = $"?param1=value1&{ContinuationTokenQueryParamName}=abc&param2=value2";
            Tuple<string, string>[] unsupportedSearchParams = null;
            string continuationToken = null;
            Dictionary<string, object> expectedRouteValues = new Dictionary<string, object>()
            {
                { "param1", new StringValues("value1") },
                { "param2", new StringValues("value2") },
                { ContinuationTokenQueryParamName, new StringValues("abc") },
            };

            TestAndValidateRouteWithQueryParameter(inputQueryString, null, unsupportedSearchParams, continuationToken, expectedRouteValues);
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

            TestAndValidateRouteWithQueryParameter(inputQueryString, null, unsupportedSearchParams, continuationToken, expectedRouteValues);
        }

        [InlineData("?_sort=a,-b")]
        [InlineData("?_sort=a,-b&_sort=-c,d")]
        [Theory]
        public void GivenSortingParameters_WhenSearchUrlIsResolved_ThenCorrectUrlShouldBeReReturned(string queryString)
        {
            TestAndValidateRouteWithQueryParameter(
                queryString,
                new[] { (new SearchParameterInfo("a", "a"), SortOrder.Ascending), (new SearchParameterInfo("b", "b"), SortOrder.Descending) },
                null,
                null,
                new Dictionary<string, object> { { KnownQueryParameterNames.Sort, "a,-b" } });
        }

        [Fact]
        public void GivenSortingParametersButWhenNoneAreApplied_WhenSearchUrlIsResolved_ThenTheUrlWillNotContainTheSortParameter()
        {
            TestAndValidateRouteWithQueryParameter(
                "?_sort=a,-b",
                null,
                null,
                null,
                new Dictionary<string, object>());
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
        public void GivenAReindexOperation_WhenOperationResultUrlIsResolved_ThenCorrectUrlShouldBeReturned()
        {
            const string id = "12345";
            const string opName = OperationsConstants.Reindex;

            _urlResolver.ResolveOperationResultUrl(opName, id);

            ValidateUrlRouteContext(
                RouteNames.GetReindexStatusById,
                routeValues =>
                {
                    Assert.Equal(id, routeValues[KnownActionParameterNames.Id]);
                });
        }

        [Fact]
        public void GivenAnUnknownOperation_WhenOperationResultUrlIsResolved_ThenOperationNotImplementedExceptionShouldBeThrown()
        {
            const string id = "12345";
            const string opName = "fakeOp";

            Assert.Throws<OperationNotImplementedException>(() => _urlResolver.ResolveOperationResultUrl(opName, id));
        }

        [Fact]
        public void GivenABundleBeingProcessed_WhenUrlIsResolvedWithQuery_ThenTheCorrectValueIsReturned()
        {
            string inputQueryString = "?param1=value1&param2=value2";
            Tuple<string, string>[] unsupportedSearchParams = null;
            string continuationToken = "continue";
            var expectedRouteValues = new Dictionary<string, object>()
            {
                { "param3", new StringValues("value3") },
                { "param4", new StringValues("value4") },
                { ContinuationTokenQueryParamName, continuationToken },
            };

            var bundleHttpContext = new DefaultHttpContext();
            bundleHttpContext.Request.QueryString = new QueryString("?param3=value3&param4=value4");
            bundleHttpContext.Request.Scheme = Scheme;
            bundleHttpContext.Request.Host = new HostString(Host);
            _bundleHttpContextAccessor.HttpContext.Returns(bundleHttpContext);

            TestAndValidateRouteWithQueryParameter(inputQueryString, null, unsupportedSearchParams, continuationToken, expectedRouteValues);
        }

        private void TestAndValidateRouteWithQueryParameter(
            string inputQueryString,
            IReadOnlyList<(SearchParameterInfo searchParameterInfo, SortOrder sortOrder)> resultSortOrder,
            Tuple<string, string>[] unsupportedSearchParams,
            string continuationToken,
            Dictionary<string, object> expectedRouteValues)
        {
            _httpContext.Request.QueryString = new QueryString(inputQueryString);

            _urlResolver.ResolveRouteUrl(unsupportedSearchParams, resultSortOrder, continuationToken);

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
