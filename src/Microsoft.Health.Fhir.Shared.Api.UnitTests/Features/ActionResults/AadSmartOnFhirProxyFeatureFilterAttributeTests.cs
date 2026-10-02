// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.ActionResults
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.SmartOnFhir)]
    public class AadSmartOnFhirProxyFeatureFilterAttributeTests
    {
        [Fact]
        public void GivenProxyDisabled_WhenActionExecuting_ThenUnauthorizedResultIsSet()
        {
            ActionExecutingContext context = CreateContext();

            CreateFilter(enableAadSmartOnFhirProxy: false).OnActionExecuting(context);

            Assert.IsType<UnauthorizedResult>(context.Result);
        }

        [Fact]
        public void GivenProxyEnabled_WhenActionExecuting_ThenRequestIsNotShortCircuited()
        {
            ActionExecutingContext context = CreateContext();

            CreateFilter(enableAadSmartOnFhirProxy: true).OnActionExecuting(context);

            Assert.Null(context.Result);
        }

        /// <summary>
        /// The filter only has an effect if it is actually applied to the controller. Guards against the
        /// filter being declared but left unreferenced, which would silently leave the proxy endpoints
        /// reachable while <see cref="SecurityConfiguration.EnableAadSmartOnFhirProxy"/> is false.
        /// </summary>
        [Fact]
        public void GivenTheProxyController_WhenInspectingItsFilters_ThenTheFeatureFilterIsApplied()
        {
            IReadOnlyList<Type> filters = GetControllerFilterTypes();

            Assert.Contains(typeof(AadSmartOnFhirProxyFeatureFilterAttribute), filters);
        }

        private static AadSmartOnFhirProxyFeatureFilterAttribute CreateFilter(bool enableAadSmartOnFhirProxy)
        {
            var securityConfiguration = new SecurityConfiguration
            {
                EnableAadSmartOnFhirProxy = enableAadSmartOnFhirProxy,
            };

            return new AadSmartOnFhirProxyFeatureFilterAttribute(Options.Create(securityConfiguration));
        }

        private static ActionExecutingContext CreateContext()
        {
            var actionContext = new ActionContext(
                new DefaultHttpContext(),
                new RouteData(),
                new ActionDescriptor());

            return new ActionExecutingContext(
                actionContext,
                new List<IFilterMetadata>(),
                new Dictionary<string, object>(),
                controller: null);
        }

        private static IReadOnlyList<Type> GetControllerFilterTypes()
        {
            return typeof(AadSmartOnFhirProxyController)
                .GetCustomAttributes(inherit: true)
                .Select(attribute => attribute switch
                {
                    TypeFilterAttribute typeFilter => typeFilter.ImplementationType,
                    ServiceFilterAttribute serviceFilter => serviceFilter.ServiceType,
                    _ => attribute.GetType(),
                })
                .ToList();
        }
    }
}
