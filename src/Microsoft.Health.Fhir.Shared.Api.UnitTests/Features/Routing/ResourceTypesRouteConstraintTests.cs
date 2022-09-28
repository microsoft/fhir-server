// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Api.Modules;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Routing
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class ResourceTypesRouteConstraintTests : RouteTestBase
    {
        [Fact]
        public async Task GivenAValidModelGetRequest_WhenRouting_ThenConstraintIsPassed()
        {
            var data = await GetRouteData(HttpMethods.Get, "/Observation/123");

            Assert.Equal(KnownRoutes.ResourceTypeById, data.Routers.OfType<Route>().First().RouteTemplate);
            Assert.True(data.Values.ContainsKey(KnownActionParameterNames.ResourceType));
            Assert.Equal("Observation", data.Values[KnownActionParameterNames.ResourceType]);
        }

        [Fact]
        public async Task GivenAValidModelGetRequestWithLowerCase_WhenRouting_ThenConstraintDoesNotAcceptLower()
        {
            var data = await GetRouteData(HttpMethods.Get, "/observation/123");

            Assert.Empty(data.Routers);
            Assert.False(data.Values.ContainsKey(KnownActionParameterNames.ResourceType));
        }

        [Fact]
        public async Task GivenAMetadataRequest_WhenRouting_ThenConstraintIsNotPassed()
        {
            var data = await GetRouteData(HttpMethods.Get, "/metadata");

            Assert.Equal(KnownRoutes.Metadata, data.Routers.OfType<Route>().First().RouteTemplate);
            Assert.False(data.Values.ContainsKey(KnownActionParameterNames.ResourceType));
        }

        [Fact]
        public async Task GivenAnUnknownModelRequest_WhenRouting_ThenConstraintIsNotPassed()
        {
            var data = await GetRouteData(HttpMethods.Get, "/RandomModel/1234");

            Assert.Empty(data.Routers);
            Assert.False(data.Values.ContainsKey(KnownActionParameterNames.ResourceType));
        }

        protected override void AddAdditionalServices(IServiceCollection builder)
        {
            new MvcModule().Load(builder);
        }
    }
}
