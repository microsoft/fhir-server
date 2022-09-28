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
    [Trait(Traits.Category, Categories.CompartmentSearch)]
    [Trait(Traits.Category, Categories.Web)]
    public class CompartmentTypesRouteConstraintTests : RouteTestBase
    {
        [Theory]
        [InlineData("/Patient/123/Observation", "Patient", "123", "Observation")]
        [InlineData("/Device/abc/Patient", "Device", "abc", "Patient")]
        [InlineData("/Encounter/asasasa/Condition", "Encounter", "asasasa", "Condition")]
        [InlineData("/Practitioner/doc1/Account", "Practitioner", "doc1", "Account")]
        [InlineData("/RelatedPerson/123/Practitioner", "RelatedPerson", "123", "Practitioner")]
        [InlineData("/Patient/123/*", "Patient", "123", "*")]
        [InlineData("/Device/123/*", "Device", "123", "*")]
        [InlineData("/Encounter/123/*", "Encounter", "123", "*")]
        [InlineData("/Practitioner/123/*", "Practitioner", "123", "*")]
        [InlineData("/RelatedPerson/xyzz/*", "RelatedPerson", "xyzz", "*")]
        public async Task GivenAValidModelGetRequest_WhenRouting_ThenConstraintIsPassed(string url, string compartmentType, string compartmentId, string resourceType)
        {
            var data = await GetRouteData(HttpMethods.Get, url);

            Assert.Equal(KnownRoutes.CompartmentTypeByResourceType, data.Routers.OfType<Route>().First().RouteTemplate);
            Assert.True(data.Values.ContainsKey(KnownActionParameterNames.ResourceType));
            Assert.True(data.Values.ContainsKey(KnownActionParameterNames.CompartmentType));
            Assert.Equal(compartmentType, data.Values[KnownActionParameterNames.CompartmentType]);
            Assert.Equal(compartmentId, data.Values[KnownActionParameterNames.Id]);
            Assert.Equal(resourceType, data.Values[KnownActionParameterNames.ResourceType]);
        }

        [Theory]
        [InlineData("/Condition/123/Observation")]
        [InlineData("/Observation/abc/Patient")]
        [InlineData("/asaadad/asasasa/Condition")]
        [InlineData("/Procedure/doc1/Account")]
        [InlineData("/Account/123/Practitioner")]
        [InlineData("/fasasfsdfdsfas/123/*")]
        [InlineData("/Allergy/123/*")]
        [InlineData("/Appointment/123/*")]
        [InlineData("/bgfdbdgfbdfbdf/123/*")]
        [InlineData("/Claim/xyzz/*")]
        public async Task GivenAValidModelGetRequestWithInvalidCompartmentTypes_WhenRouting_ThenConstraintIsNotPassed(string url)
        {
            var data = await GetRouteData(HttpMethods.Get, url);

            Assert.Empty(data.Routers);
            Assert.False(data.Values.ContainsKey(KnownActionParameterNames.ResourceType));
            Assert.False(data.Values.ContainsKey(KnownActionParameterNames.CompartmentType));
        }

        [Theory]
        [InlineData("/Patient/123/aasdas")]
        [InlineData("/Device/abc/etrvfdv")]
        [InlineData("/Encounter/asasasa/ccs_sdfsdfsd")]
        [InlineData("/Practitioner/doc1/sdjlfasdjfjas")]
        [InlineData("/RelatedPerson/123/wsvdvcdsdsfgvfsd;vg;")]
        public async Task GivenAValidModelGetRequestWithInvalidResourceTypeAndValidCompartmentTypes_WhenRouting_ThenConstraintIsNotPassed(string url)
        {
            var data = await GetRouteData(HttpMethods.Get, url);

            Assert.Empty(data.Routers);
            Assert.False(data.Values.ContainsKey(KnownActionParameterNames.ResourceType));
            Assert.False(data.Values.ContainsKey(KnownActionParameterNames.CompartmentType));
        }

        [Theory]
        [InlineData("/patient/123/Condition")]
        [InlineData("/DevIce/abc/Account")]
        [InlineData("/EncoUnter/asasasa/Claim")]
        [InlineData("/PractitioneR/doc1/Procedure")]
        [InlineData("/RelATedPeRson/123/Appointment")]
        public async Task GivenAValidModelGetRequestWithInvalidCompartmentTypeCasing_WhenRouting_ThenConstraintIsNotPassed(string url)
        {
            var data = await GetRouteData(HttpMethods.Get, url);

            Assert.Empty(data.Routers);
            Assert.False(data.Values.ContainsKey(KnownActionParameterNames.ResourceType));
            Assert.False(data.Values.ContainsKey(KnownActionParameterNames.CompartmentType));
        }

        protected override void AddAdditionalServices(IServiceCollection builder)
        {
            new MvcModule().Load(builder);
        }
    }
}
