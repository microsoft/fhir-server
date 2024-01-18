// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public sealed class QueryLatencyOverEfficiencyFilterAttributeTests
    {
        private readonly IFhirRuntimeConfiguration _azureApiForFhirConfiguration = new AzureApiForFhirRuntimeConfiguration();
        private readonly IFhirRuntimeConfiguration _azureHealthDataServicesFhirConfiguration = new AzureHealthDataServicesRuntimeConfiguration();

        [Fact]
        public void GivenAValidHttpContextForAzureApiForFhir_WhenItContainsALatencyOverEfficiencyFlag_ThenFhirContextIsDecorated()
        {
            var httpRequest = GetFakeHttpContext(isLatencyOverEfficiencyEnabled: true);

            var filter = new QueryLatencyOverEfficiencyFilterAttribute(httpRequest.RequestContext, _azureApiForFhirConfiguration);
            filter.OnActionExecuting(httpRequest.ActionContext);

            var fhirContextPropertyBag = httpRequest.RequestContext.RequestContext.Properties;

            Assert.True(fhirContextPropertyBag.ContainsKey(KnownQueryParameterNames.OptimizeConcurrency));
            Assert.Equal(true, fhirContextPropertyBag[KnownQueryParameterNames.OptimizeConcurrency]);
        }

        [Fact]
        public void GivenAValidHttpContextForAzureHealthDataService_WhenItContainsALatencyOverEfficiencyFlag_ThenFhirContextIsNotDecorated()
        {
            // The latency-over-efficiency flag is only applicable to Azure API for FHIR.

            var httpRequest = GetFakeHttpContext(isLatencyOverEfficiencyEnabled: true);

            var filter = new QueryLatencyOverEfficiencyFilterAttribute(httpRequest.RequestContext, _azureHealthDataServicesFhirConfiguration);
            filter.OnActionExecuting(httpRequest.ActionContext);

            var fhirContextPropertyBag = httpRequest.RequestContext.RequestContext.Properties;

            Assert.False(fhirContextPropertyBag.ContainsKey(KnownQueryParameterNames.OptimizeConcurrency));
        }

        [Fact]
        public void GivenAValidHttpContext_WhenItDoesNotContainALatencyOverEfficiencyFlag_ThenFhirContextIsClean()
        {
            var httpRequest = GetFakeHttpContext(isLatencyOverEfficiencyEnabled: false);

            var filter = new QueryLatencyOverEfficiencyFilterAttribute(httpRequest.RequestContext, _azureApiForFhirConfiguration);
            filter.OnActionExecuting(httpRequest.ActionContext);

            var fhirContextPropertyBag = httpRequest.RequestContext.RequestContext.Properties;

            Assert.False(fhirContextPropertyBag.ContainsKey(KnownQueryParameterNames.OptimizeConcurrency));
        }

        private static (RequestContextAccessor<IFhirRequestContext> RequestContext, ActionExecutingContext ActionContext) GetFakeHttpContext(bool isLatencyOverEfficiencyEnabled)
        {
            var httpContext = new DefaultHttpContext();

            if (isLatencyOverEfficiencyEnabled)
            {
                httpContext.Request.Headers[KnownHeaders.QueryLatencyOverEfficiency] = "true";
            }

            ActionExecutingContext context = new ActionExecutingContext(
                new ActionContext(
                    httpContext,
                    new RouteData(),
                    new ActionDescriptor()),
                new List<IFilterMetadata>(),
                actionArguments: new Dictionary<string, object>(),
                FilterTestsHelper.CreateMockFhirController());

            DefaultFhirRequestContext fhirRequestContext = new DefaultFhirRequestContext();

            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

            return (fhirRequestContextAccessor, context);
        }
    }
}
