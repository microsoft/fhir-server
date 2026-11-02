// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Api.Features.Filters.Metrics;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters.Metrics
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public sealed class MetricEmitterAttributeTests
    {
        [Fact]
        public async Task SearchEndpointMetricEmitterAttribute_WhenCalled_EmittsMetrics()
        {
            ISearchMetricHandler metricHandler = Substitute.For<ISearchMetricHandler>();
            metricHandler.EmitLatency(Arg.Any<SearchMetricNotification>());

            var attribute = new SearchEndpointMetricEmitterAttribute(
                metricHandler,
                NullLogger<SearchEndpointMetricEmitterAttribute>.Instance);

            attribute.OnActionExecuting(null);
            await Task.Delay(100);
            attribute.OnActionExecuted(null);

            metricHandler.Received(1).EmitLatency(Arg.Any<SearchMetricNotification>());
        }

        [Fact]
        public async Task CrudEndpointMetricEmitterAttribute_WhenCalled_EmittsMetrics()
        {
            ICrudMetricHandler metricHandler = Substitute.For<ICrudMetricHandler>();
            metricHandler.EmitLatency(Arg.Any<CrudMetricNotification>());

            var attribute = new CrudEndpointMetricEmitterAttribute(metricHandler);

            attribute.OnActionExecuting(null);
            await Task.Delay(100);
            attribute.OnActionExecuted(null);

            metricHandler.Received(1).EmitLatency(Arg.Any<CrudMetricNotification>());
        }

        [Theory]
        [InlineData("FhirController", "SystemHistory", SearchComplexity.High)]
        [InlineData("Everything", "PatientEverythingById", SearchComplexity.Medium)]
        [InlineData("Includes", "Search", SearchComplexity.Medium)]
        [InlineData("DocRef", "Search", SearchComplexity.Medium)]
        [InlineData("FhirController", "SearchByResourceType", SearchComplexity.Low)]
        [InlineData("FhirController", "Search", SearchComplexity.Low)]
        [InlineData("FhirController", "TypeHistory", SearchComplexity.Low)]
        [InlineData("FhirController", "History", SearchComplexity.Low)]
        public void DetermineComplexity_GivenRouteValues_ReturnsExpectedComplexity(
            string controller,
            string action,
            SearchComplexity expected)
        {
            var routeValues = new Dictionary<string, string>
            {
                { "controller", controller },
                { "action", action },
            };

            var actionDescriptor = new ActionDescriptor();
            foreach (var kv in routeValues)
            {
                actionDescriptor.RouteValues[kv.Key] = kv.Value;
            }

            var httpContext = new DefaultHttpContext();
            var actionContext = new ActionContext(httpContext, new RouteData(), actionDescriptor);
            var context = new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), controller: null);

            SearchComplexity result = SearchEndpointMetricEmitterAttribute.DetermineComplexity(context);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void DetermineComplexity_GivenMultipleIncludes_ReturnsMedium()
        {
            var actionDescriptor = new ActionDescriptor();
            actionDescriptor.RouteValues["controller"] = "FhirController";
            actionDescriptor.RouteValues["action"] = "SearchByResourceType";

            var httpContext = new DefaultHttpContext();
            httpContext.Request.QueryString = new QueryString("?_include=Patient:organization&_include=Patient:general-practitioner");
            var actionContext = new ActionContext(httpContext, new RouteData(), actionDescriptor);
            var context = new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), controller: null);

            SearchComplexity result = SearchEndpointMetricEmitterAttribute.DetermineComplexity(context);

            Assert.Equal(SearchComplexity.Medium, result);
        }

        [Fact]
        public void DetermineComplexity_GivenSingleInclude_ReturnsStandard()
        {
            var actionDescriptor = new ActionDescriptor();
            actionDescriptor.RouteValues["controller"] = "FhirController";
            actionDescriptor.RouteValues["action"] = "SearchByResourceType";

            var httpContext = new DefaultHttpContext();
            httpContext.Request.QueryString = new QueryString("?_include=Patient:organization");
            var actionContext = new ActionContext(httpContext, new RouteData(), actionDescriptor);
            var context = new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), controller: null);

            SearchComplexity result = SearchEndpointMetricEmitterAttribute.DetermineComplexity(context);

            Assert.Equal(SearchComplexity.Low, result);
        }

        [Fact]
        public void DetermineComplexity_GivenNullContext_ReturnsStandard()
        {
            SearchComplexity result = SearchEndpointMetricEmitterAttribute.DetermineComplexity(null);

            Assert.Equal(SearchComplexity.Low, result);
        }

        [Theory]
        [InlineData("?_sort=birthdate")]
        [InlineData("?_sort=-birthdate")]
        [InlineData("?_sort=_lastUpdated")]
        [InlineData("?_sort=-_lastUpdated")]
        [InlineData("?_sort=_id")]
        [InlineData("?_sort=birthdate,_lastUpdated")]
        [InlineData("?name=smith&_sort=family")]
        public void DetermineComplexity_GivenSortParameter_ReturnsMedium(string queryString)
        {
            ActionExecutedContext context = CreateContext("FhirController", "SearchByResourceType", queryString);

            SearchComplexity result = SearchEndpointMetricEmitterAttribute.DetermineComplexity(context);

            Assert.Equal(SearchComplexity.Medium, result);
        }

        [Theory]
        [InlineData("")]
        [InlineData("?name=smith")]
        [InlineData("?_sort=")]
        public void DetermineComplexity_GivenNoSortParameter_ReturnsLow(string queryString)
        {
            ActionExecutedContext context = CreateContext("FhirController", "SearchByResourceType", queryString);

            SearchComplexity result = SearchEndpointMetricEmitterAttribute.DetermineComplexity(context);

            Assert.Equal(SearchComplexity.Low, result);
        }

        [Fact]
        public void DetermineComplexity_GivenSystemHistoryWithSort_ReturnsHigh()
        {
            ActionExecutedContext context = CreateContext("FhirController", "SystemHistory", "?_sort=_lastUpdated");

            SearchComplexity result = SearchEndpointMetricEmitterAttribute.DetermineComplexity(context);

            Assert.Equal(SearchComplexity.High, result);
        }

        private static ActionExecutedContext CreateContext(string controller, string action, string queryString)
        {
            var actionDescriptor = new ActionDescriptor();
            actionDescriptor.RouteValues["controller"] = controller;
            actionDescriptor.RouteValues["action"] = action;

            var httpContext = new DefaultHttpContext();
            httpContext.Request.QueryString = new QueryString(queryString);

            var actionContext = new ActionContext(httpContext, new RouteData(), actionDescriptor);
            return new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), controller: null);
        }
    }
}
