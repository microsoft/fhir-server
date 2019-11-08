// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters
{
    public class FhirRequestContextRouteNameFilterAttributeTests
    {
        private readonly ActionExecutingContext _context;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        private readonly IFhirRequestContext _fhirRequestContext = Substitute.For<IFhirRequestContext>();
        private readonly IAuditEventTypeMapping _auditEventTypeMapping = Substitute.For<IAuditEventTypeMapping>();
        private readonly string _correlationId = Guid.NewGuid().ToString();
        private readonly HttpContext _httpContext = new DefaultHttpContext();
        private const string ControllerName = "controller";
        private const string ActionName = "actionName";
        private const string RouteName = "routeName";
        private const string NormalAuditEventType = "event-name";

        public FhirRequestContextRouteNameFilterAttributeTests()
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

            _context = new ActionExecutingContext(
                new ActionContext(_httpContext, new RouteData(), controllerActionDescriptor),
                new List<IFilterMetadata>(),
                new Dictionary<string, object>(),
                FilterTestsHelper.CreateMockFhirController());

            _fhirRequestContext.CorrelationId.Returns(_correlationId);
            _fhirRequestContextAccessor.FhirRequestContext.Returns(_fhirRequestContext);
        }

        [Fact]
        public void GivenNormalRequest_WhenExecutingAnAction_ThenValuesShouldBeSetOnFhirRequestContext()
        {
            _auditEventTypeMapping.GetAuditEventType(ControllerName, ActionName).Returns(NormalAuditEventType);

            var filter = new FhirRequestContextRouteNameFilterAttribute(_fhirRequestContextAccessor, _auditEventTypeMapping);

            filter.OnActionExecuting(_context);

            Assert.NotNull(_fhirRequestContextAccessor.FhirRequestContext.AuditEventType);
            Assert.Equal(NormalAuditEventType, _fhirRequestContextAccessor.FhirRequestContext.AuditEventType);
            Assert.Equal(RouteName, _fhirRequestContextAccessor.FhirRequestContext.RouteName);
        }

        [Fact]
        public void GivenNormalBatchRequest_WhenExecutingAnAction_ThenValuesShouldBeSetOnFhirRequestContext()
        {
            _auditEventTypeMapping.GetAuditEventType(ControllerName, ActionName).Returns(AuditEventSubType.BundlePost);

            _context.ActionArguments.Add(KnownActionParameterNames.Bundle, Samples.GetDefaultBatch().ToPoco<Bundle>());

            var filter = new FhirRequestContextRouteNameFilterAttribute(_fhirRequestContextAccessor, _auditEventTypeMapping);

            filter.OnActionExecuting(_context);

            Assert.NotNull(_fhirRequestContextAccessor.FhirRequestContext.AuditEventType);
            Assert.Equal(AuditEventSubType.Batch, _fhirRequestContextAccessor.FhirRequestContext.AuditEventType);
            Assert.Equal(RouteName, _fhirRequestContextAccessor.FhirRequestContext.RouteName);
        }

        [Fact]
        public void GivenNormalTransactionRequest_WhenExecutingAnAction_ThenValuesShouldBeSetOnFhirRequestContext()
        {
            _auditEventTypeMapping.GetAuditEventType(ControllerName, ActionName).Returns(AuditEventSubType.BundlePost);

            _context.ActionArguments.Add(KnownActionParameterNames.Bundle, Samples.GetDefaultTransaction().ToPoco<Bundle>());

            var filter = new FhirRequestContextRouteNameFilterAttribute(_fhirRequestContextAccessor, _auditEventTypeMapping);

            filter.OnActionExecuting(_context);

            Assert.NotNull(_fhirRequestContextAccessor.FhirRequestContext.AuditEventType);
            Assert.Equal(AuditEventSubType.Transaction, _fhirRequestContextAccessor.FhirRequestContext.AuditEventType);
            Assert.Equal(RouteName, _fhirRequestContextAccessor.FhirRequestContext.RouteName);
        }

        [Fact]
        public void GivenABundleRequestWithNoArgumentRequest_WhenExecutingAnAction_ThenValuesShouldBeSetOnFhirRequestContext()
        {
            _auditEventTypeMapping.GetAuditEventType(ControllerName, ActionName).Returns(AuditEventSubType.BundlePost);

            var filter = new FhirRequestContextRouteNameFilterAttribute(_fhirRequestContextAccessor, _auditEventTypeMapping);

            filter.OnActionExecuting(_context);

            Assert.NotNull(_fhirRequestContextAccessor.FhirRequestContext.AuditEventType);
            Assert.Equal(AuditEventSubType.BundlePost, _fhirRequestContextAccessor.FhirRequestContext.AuditEventType);
            Assert.Equal(RouteName, _fhirRequestContextAccessor.FhirRequestContext.RouteName);
        }

        [Fact]
        public void GivenABundleRequestWithANonBundleResourceRequest_WhenExecutingAnAction_ThenValuesShouldBeSetOnFhirRequestContext()
        {
            _auditEventTypeMapping.GetAuditEventType(ControllerName, ActionName).Returns(AuditEventSubType.BundlePost);

            _context.ActionArguments.Add(KnownActionParameterNames.Bundle, Samples.GetDefaultObservation().ToPoco<Observation>());

            var filter = new FhirRequestContextRouteNameFilterAttribute(_fhirRequestContextAccessor, _auditEventTypeMapping);

            filter.OnActionExecuting(_context);

            Assert.NotNull(_fhirRequestContextAccessor.FhirRequestContext.AuditEventType);
            Assert.Equal(AuditEventSubType.BundlePost, _fhirRequestContextAccessor.FhirRequestContext.AuditEventType);
            Assert.Equal(RouteName, _fhirRequestContextAccessor.FhirRequestContext.RouteName);
        }
    }
}
