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
            ExecuteAndValidateFilter(NormalAuditEventType, NormalAuditEventType);
        }

        [Fact]
        public void GivenNormalBatchRequest_WhenExecutingAnAction_ThenValuesShouldBeSetOnFhirRequestContext()
        {
            _context.ActionArguments.Add(KnownActionParameterNames.Bundle, Samples.GetDefaultBatch().ToPoco<Hl7.Fhir.Model.Bundle>());
            ExecuteAndValidateFilter(AuditEventSubType.BundlePost, AuditEventSubType.Batch);
        }

        [Fact]
        public void GivenNormalTransactionRequest_WhenExecutingAnAction_ThenValuesShouldBeSetOnFhirRequestContext()
        {
            _context.ActionArguments.Add(KnownActionParameterNames.Bundle, Samples.GetDefaultTransaction().ToPoco<Hl7.Fhir.Model.Bundle>());
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
            _context.ActionArguments.Add(KnownActionParameterNames.Bundle, Samples.GetDefaultObservation().ToPoco<Observation>());

            ExecuteAndValidateFilter(AuditEventSubType.BundlePost, AuditEventSubType.BundlePost);
        }

        private void ExecuteAndValidateFilter(string auditEventTypeFromMapping, string expectedAuditEventType)
        {
            _auditEventTypeMapping.GetAuditEventType(ControllerName, ActionName).Returns(auditEventTypeFromMapping);

            var filter = new FhirRequestContextRouteNameFilterAttribute(_fhirRequestContextAccessor, _auditEventTypeMapping);

            filter.OnActionExecuting(_context);

            Assert.NotNull(_fhirRequestContextAccessor.FhirRequestContext.AuditEventType);
            Assert.Equal(expectedAuditEventType, _fhirRequestContextAccessor.FhirRequestContext.AuditEventType);
            Assert.Equal(RouteName, _fhirRequestContextAccessor.FhirRequestContext.RouteName);
        }
    }
}
