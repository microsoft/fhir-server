// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class FhirRequestContextRouteDataPopulatingFilterAttributeTests
    {
        private readonly ActionExecutingContext _actionExecutingContext;
        private readonly ActionExecutedContext _actionExecutedContext;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        private readonly DefaultFhirRequestContext _fhirRequestContext = new DefaultFhirRequestContext();
        private readonly IAuditEventTypeMapping _auditEventTypeMapping = Substitute.For<IAuditEventTypeMapping>();
        private readonly IAuthorizationService<DataActions> _authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
        private readonly SecurityConfiguration _securityConfiguration = new SecurityConfiguration();
        private readonly string _correlationId = Guid.NewGuid().ToString();
        private readonly HttpContext _httpContext = new DefaultHttpContext();
        private const string ControllerName = "controller";
        private const string ActionName = "actionName";
        private const string RouteName = "routeName";
        private const string NormalAuditEventType = "event-name";

        private readonly FhirRequestContextRouteDataPopulatingFilterAttribute _filterAttribute;

        public FhirRequestContextRouteDataPopulatingFilterAttributeTests()
        {
            var controllerActionDescriptor = new ControllerActionDescriptor
            {
                DisplayName = "Executing Context Test Descriptor",
                ActionName = ActionName,
                ControllerName = ControllerName,
                AttributeRouteInfo = new AttributeRouteInfo
                {
                    Name = RouteName,
                },
            };

            _actionExecutingContext = new ActionExecutingContext(
                new ActionContext(_httpContext, new RouteData(), controllerActionDescriptor),
                new List<IFilterMetadata>(),
                new Dictionary<string, object>(),
                FilterTestsHelper.CreateMockFhirController());

            _actionExecutedContext = new ActionExecutedContext(
                new ActionContext(_httpContext, new RouteData(), controllerActionDescriptor),
                new List<IFilterMetadata>(),
                FilterTestsHelper.CreateMockFhirController());

            _fhirRequestContext.CorrelationId = _correlationId;
            _fhirRequestContextAccessor.RequestContext.Returns(_fhirRequestContext);

            _filterAttribute = new FhirRequestContextRouteDataPopulatingFilterAttribute(
                _fhirRequestContextAccessor,
                _auditEventTypeMapping,
                NullLogger<FhirRequestContextRouteDataPopulatingFilterAttribute>.Instance,
                _authorizationService,
                Options.Create(_securityConfiguration));
        }

        [Fact]
        public void GivenNormalRequest_WhenExecutingAnAction_ThenValuesShouldBeSetOnFhirRequestContext()
        {
            ExecuteAndValidateFilter(NormalAuditEventType, NormalAuditEventType);
        }

        [Fact]
        public void GivenNormalBatchRequest_WhenExecutingAnAction_ThenValuesShouldBeSetOnFhirRequestContext()
        {
            _actionExecutingContext.ActionArguments.Add(KnownActionParameterNames.Bundle, Samples.GetDefaultBatch().ToPoco<Hl7.Fhir.Model.Bundle>());
            ExecuteAndValidateFilter(AuditEventSubType.BundlePost, AuditEventSubType.Batch);
        }

        [Fact]
        public void GivenNormalTransactionRequest_WhenExecutingAnAction_ThenValuesShouldBeSetOnFhirRequestContext()
        {
            _actionExecutingContext.ActionArguments.Add(KnownActionParameterNames.Bundle, Samples.GetDefaultTransaction().ToPoco<Hl7.Fhir.Model.Bundle>());
            ExecuteAndValidateFilter(AuditEventSubType.BundlePost, AuditEventSubType.Transaction);
        }

        [Fact]
        public void GivenABundleRequestWithNoArgumentRequest_WhenExecutingAnAction_ThenValuesShouldBeSetOnFhirRequestContext()
        {
            ExecuteAndValidateFilter(AuditEventSubType.BundlePost, AuditEventSubType.BundlePost);
        }

        [Fact]
        public void GivenABundleRequestWithANonBundleResourceRequest_WhenExecutingAnAction_ThenValuesShouldBeSetOnFhirRequestContext()
        {
            _actionExecutingContext.ActionArguments.Add(KnownActionParameterNames.Bundle, Samples.GetDefaultObservation().ToPoco<Observation>());

            ExecuteAndValidateFilter(AuditEventSubType.BundlePost, AuditEventSubType.BundlePost);
        }

        [Fact]
        public void GivenANonResourceActionResult_WhenExecutedAnAction_ThenResourceTypeShouldBeSet()
        {
            _actionExecutedContext.Result = new StatusCodeResult(200);

            _filterAttribute.OnActionExecuted(_actionExecutedContext);

            Assert.Null(_fhirRequestContext.ResourceType);
        }

        [Fact]
        public async Task GivenPartialIndexHeader_WhenAuthorizationIsDisabled_ThenFhirContextPropertySet()
        {
            SetPartialIndexHeader();

            await ExecuteFilterAsync();

            Assert.True(_fhirRequestContext.IncludePartiallyIndexedSearchParams);
            await _authorizationService.DidNotReceive().CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenPartialIndexHeaderAndReindexPermission_WhenSearchRequest_ThenFhirContextPropertySet()
        {
            EnableAuthorization();
            _fhirRequestContext.Principal = new ClaimsPrincipal(new ClaimsIdentity("test"));
            _authorizationService.CheckAccess(DataActions.Reindex, Arg.Any<CancellationToken>()).Returns(DataActions.Reindex);
            SetPartialIndexHeader();

            await ExecuteFilterAsync();

            Assert.True(_fhirRequestContext.IncludePartiallyIndexedSearchParams);
            Assert.True(_httpContext.Request.Headers.ContainsKey(KnownHeaders.PartiallyIndexedParamsHeaderName));
        }

        [Fact]
        public async Task GivenPartialIndexHeaderWithoutReindexPermission_WhenSearchRequest_ThenHeaderIsIgnored()
        {
            EnableAuthorization();
            _fhirRequestContext.Principal = new ClaimsPrincipal(new ClaimsIdentity("test"));
            _authorizationService.CheckAccess(DataActions.Reindex, Arg.Any<CancellationToken>()).Returns(DataActions.None);
            SetPartialIndexHeader();
            _fhirRequestContext.RequestHeaders = new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                [KnownHeaders.PartiallyIndexedParamsHeaderName] = "true",
            };

            await ExecuteFilterAsync();

            Assert.False(_fhirRequestContext.IncludePartiallyIndexedSearchParams);
            Assert.False(_httpContext.Request.Headers.ContainsKey(KnownHeaders.PartiallyIndexedParamsHeaderName));
            Assert.False(_fhirRequestContext.RequestHeaders.ContainsKey(KnownHeaders.PartiallyIndexedParamsHeaderName));
        }

        [Fact]
        public async Task GivenPartialIndexHeaderAndNoPrincipal_WhenAuthorizationIsEnabled_ThenHeaderIsIgnored()
        {
            EnableAuthorization();
            SetPartialIndexHeader();

            await ExecuteFilterAsync();

            Assert.False(_fhirRequestContext.IncludePartiallyIndexedSearchParams);
            Assert.False(_httpContext.Request.Headers.ContainsKey(KnownHeaders.PartiallyIndexedParamsHeaderName));
            await _authorizationService.DidNotReceive().CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>());
        }

        private void EnableAuthorization()
        {
            _securityConfiguration.Enabled = true;
            _securityConfiguration.Authorization.Enabled = true;
        }

        private void SetPartialIndexHeader()
        {
            _httpContext.Request.Headers[KnownHeaders.PartiallyIndexedParamsHeaderName] = new Microsoft.Extensions.Primitives.StringValues(new string[] { "true" });
        }

        private Task ExecuteFilterAsync()
        {
            return _filterAttribute.OnActionExecutionAsync(_actionExecutingContext, () => Task.FromResult(_actionExecutedContext));
        }

        private void ExecuteAndValidateFilter(string auditEventTypeFromMapping, string expectedAuditEventType)
        {
            _auditEventTypeMapping.GetAuditEventType(ControllerName, ActionName).Returns(auditEventTypeFromMapping);

            _filterAttribute.OnActionExecuting(_actionExecutingContext);

            Assert.NotNull(_fhirRequestContextAccessor.RequestContext.AuditEventType);
            Assert.Equal(expectedAuditEventType, _fhirRequestContextAccessor.RequestContext.AuditEventType);
            Assert.Equal(RouteName, _fhirRequestContextAccessor.RequestContext.RouteName);
        }
    }
}
