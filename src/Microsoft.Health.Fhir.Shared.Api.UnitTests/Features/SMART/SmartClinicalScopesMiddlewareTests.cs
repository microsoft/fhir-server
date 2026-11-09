// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Smart;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;
using Claim = System.Security.Claims.Claim;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Smart
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.SmartOnFhir)]
    public class SmartClinicalScopesMiddlewareTests
    {
        private readonly SmartClinicalScopesMiddleware _smartClinicalScopesMiddleware;
        private IAuthorizationService<DataActions> _authorizationService;
        private ILogger<SmartClinicalScopesMiddleware> _logger = Substitute.For<ILogger<SmartClinicalScopesMiddleware>>();

        public SmartClinicalScopesMiddlewareTests()
        {
            _smartClinicalScopesMiddleware = new SmartClinicalScopesMiddleware(
                httpContext => Task.CompletedTask, _logger);
        }

        [Theory]
        [MemberData(nameof(GetTestScopesAndRoles))]
        public async Task GivenSmartScopesSplitAcrossClaims_WhenInvoked_ThenScopeParsedandAddedtoContext(string scopes, string claims, ICollection<ScopeRestriction> expectedScopeRestrictions)
        {
            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            var fhirRequestContext = new DefaultFhirRequestContext();

            fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            fhirConfiguration.Security.Enabled = true;
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.Enabled = true;
            await LoadRoles(authorizationConfiguration);

            var fhirUserClaim = new Claim(authorizationConfiguration.FhirUserClaim, "https://fhirServer/Patient/foo");
            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, claims);
            var rolesSmartUserClaim = new Claim(authorizationConfiguration.RolesClaim, "smartUser");

            foreach (string singleClaim in authorizationConfiguration.ScopesClaim)
            {
                var scopesClaim = new Claim(singleClaim, scopes);
                var claimsIdentity = new ClaimsIdentity(new List<Claim>() { scopesClaim, rolesClaim, fhirUserClaim, rolesSmartUserClaim });
                var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

                httpContext.User = expectedPrincipal;
                fhirRequestContext.Principal = expectedPrincipal;

                _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

                await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService);

                Assert.Equal(expectedScopeRestrictions, fhirRequestContext.AccessControlContext.AllowedResourceActions);
            }
        }

        [Theory]
        [MemberData(nameof(GetTestScopes))]
        public async Task GivenSmartScope_WhenInvoked_ThenScopeParsedandAddedtoContext(string scopes, ICollection<ScopeRestriction> expectedScopeRestrictions)
        {
            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            var fhirRequestContext = new DefaultFhirRequestContext();

            fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            fhirConfiguration.Security.Enabled = true;
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.Enabled = true;
            await LoadRoles(authorizationConfiguration);

            var fhirUserClaim = new Claim(authorizationConfiguration.FhirUserClaim, "https://fhirServer/Patient/foo");
            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, "smartUser");

            foreach (string singleClaim in authorizationConfiguration.ScopesClaim)
            {
                var scopesClaim = new Claim(singleClaim, scopes);
                var claimsIdentity = new ClaimsIdentity(new List<Claim>() { scopesClaim, rolesClaim, fhirUserClaim });
                var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

                httpContext.User = expectedPrincipal;
                fhirRequestContext.Principal = expectedPrincipal;

                _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

                await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService);

                Assert.Equal(expectedScopeRestrictions, fhirRequestContext.AccessControlContext.AllowedResourceActions);
            }
        }

        [Theory]
        [MemberData(nameof(GetTestScopes))]
        public async Task GivenSmartRawScope_WhenInvoked_ThenScopeParsedandAddedtoContext(string scopes, ICollection<ScopeRestriction> expectedScopeRestrictions)
        {
            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            var fhirRequestContext = new DefaultFhirRequestContext();

            fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            fhirConfiguration.Security.Enabled = true;
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.Enabled = true;
            await LoadRoles(authorizationConfiguration);

            var fhirUserClaim = new Claim(authorizationConfiguration.FhirUserClaim, "https://fhirServer/Patient/foo");
            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, "smartUser");

            var rawScopesClaim = new Claim("raw_scope", scopes);
            var claimsIdentity = new ClaimsIdentity(new List<Claim>() { rawScopesClaim, rolesClaim, fhirUserClaim });
            var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

            httpContext.User = expectedPrincipal;
            fhirRequestContext.Principal = expectedPrincipal;

            _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

            await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService);

            Assert.Equal(expectedScopeRestrictions, fhirRequestContext.AccessControlContext.AllowedResourceActions);
        }

        [Theory]
        [MemberData(nameof(GetMixedSmartScopeVersions))]
        public async Task GivenMixedSmartScopeVersions_WhenInvoked_ThenBadRequestIsThrown(string scopes)
        {
            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            fhirConfiguration.Security.Enabled = true;
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.Enabled = true;
            await LoadRoles(authorizationConfiguration);

            var fhirUserClaim = new Claim(authorizationConfiguration.FhirUserClaim, "https://fhirServer/Patient/foo");
            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, "smartUser");

            using var stream = new MemoryStream();
            foreach (string singleClaim in authorizationConfiguration.ScopesClaim)
            {
                stream.SetLength(0);
                httpContext.Response.Body = stream;

                var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

                var fhirRequestContext = new DefaultFhirRequestContext()
                {
                    CorrelationId = Guid.NewGuid().ToString(),
                };

                fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

                var scopesClaim = new Claim(singleClaim, scopes);
                var claimsIdentity = new ClaimsIdentity(new List<Claim>() { scopesClaim, rolesClaim, fhirUserClaim });
                var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

                httpContext.User = expectedPrincipal;
                fhirRequestContext.Principal = expectedPrincipal;

                _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

                await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService);
                ValidateErrorResponse(httpContext);
            }
        }

        [Theory]
        [MemberData(nameof(GetMixedSmartScopeContexts))]
        public async Task GivenMixedSmartScopeContexts_WhenInvoked_ThenBadRequestIsThrown(string scopes)
        {
            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            fhirConfiguration.Security.Enabled = true;
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.Enabled = true;
            await LoadRoles(authorizationConfiguration);

            var fhirUserClaim = new Claim(authorizationConfiguration.FhirUserClaim, "https://fhirServer/Patient/foo");
            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, "smartUser");

            using var stream = new MemoryStream();
            foreach (string singleClaim in authorizationConfiguration.ScopesClaim)
            {
                stream.SetLength(0);
                httpContext.Response.Body = stream;

                var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

                var fhirRequestContext = new DefaultFhirRequestContext()
                {
                    CorrelationId = Guid.NewGuid().ToString(),
                };

                fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

                var scopesClaim = new Claim(singleClaim, scopes);
                var claimsIdentity = new ClaimsIdentity(new List<Claim>() { scopesClaim, rolesClaim, fhirUserClaim });
                var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

                httpContext.User = expectedPrincipal;
                fhirRequestContext.Principal = expectedPrincipal;

                _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

                await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService);
                ValidateErrorResponse(
                    httpContext,
                    new[] { Api.Resources.MixedSMARTScopeContextsAreNotAllowed });
            }
        }

        [Theory]
        [MemberData(nameof(GetScopesWithModifiers))]
        public async Task GivenSmartScopeWithModifier_WhenInvoked_ThenBadRequestIsThrownWithModifierMessage(string scopes)
        {
            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            fhirConfiguration.Security.Enabled = true;
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.Enabled = true;
            await LoadRoles(authorizationConfiguration);

            var fhirUserClaim = new Claim(authorizationConfiguration.FhirUserClaim, "https://fhirServer/Patient/foo");
            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, "smartUser");

            using var stream = new MemoryStream();
            foreach (string singleClaim in authorizationConfiguration.ScopesClaim)
            {
                stream.SetLength(0);
                httpContext.Response.Body = stream;

                var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

                var fhirRequestContext = new DefaultFhirRequestContext()
                {
                    CorrelationId = Guid.NewGuid().ToString(),
                };

                fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

                var scopesClaim = new Claim(singleClaim, scopes);
                var claimsIdentity = new ClaimsIdentity(new List<Claim>() { scopesClaim, rolesClaim, fhirUserClaim });
                var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

                httpContext.User = expectedPrincipal;
                fhirRequestContext.Principal = expectedPrincipal;

                _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

                await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService);
                ValidateErrorResponse(
                    httpContext,
                    new[]
                    {
                        "modifiers are not supported",
                        "unsupported modifier",
                    },
                    true);
            }
        }

        [Theory]
        [MemberData(nameof(GetScopesWithChainedSearch))]
        public async Task GivenSmartScopeWithChainedSearch_WhenInvoked_ThenBadRequestIsThrownWithChainedSearchMessage(string scopes)
        {
            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            fhirConfiguration.Security.Enabled = true;
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.Enabled = true;
            await LoadRoles(authorizationConfiguration);

            var fhirUserClaim = new Claim(authorizationConfiguration.FhirUserClaim, "https://fhirServer/Patient/foo");
            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, "smartUser");

            using var stream = new MemoryStream();
            foreach (string singleClaim in authorizationConfiguration.ScopesClaim)
            {
                stream.SetLength(0);
                httpContext.Response.Body = stream;

                var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

                var fhirRequestContext = new DefaultFhirRequestContext()
                {
                    CorrelationId = Guid.NewGuid().ToString(),
                };

                fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

                var scopesClaim = new Claim(singleClaim, scopes);
                var claimsIdentity = new ClaimsIdentity(new List<Claim>() { scopesClaim, rolesClaim, fhirUserClaim });
                var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

                httpContext.User = expectedPrincipal;
                fhirRequestContext.Principal = expectedPrincipal;

                _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

                await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService);
                ValidateErrorResponse(
                    httpContext,
                    new[]
                    {
                        "Chained search parameters are not supported",
                        "chained search parameter",
                    },
                    true);
            }
        }

        [Theory]
        [MemberData(nameof(GetScopesWithIncludes))]
        public async Task GivenSmartScopeWithIncludeParameter_WhenInvoked_ThenBadRequestIsThrownWithIncludeMessage(string scopes)
        {
            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            fhirConfiguration.Security.Enabled = true;
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.Enabled = true;
            await LoadRoles(authorizationConfiguration);

            var fhirUserClaim = new Claim(authorizationConfiguration.FhirUserClaim, "https://fhirServer/Patient/foo");
            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, "smartUser");

            using var stream = new MemoryStream();
            foreach (string singleClaim in authorizationConfiguration.ScopesClaim)
            {
                stream.SetLength(0);
                httpContext.Response.Body = stream;

                var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

                var fhirRequestContext = new DefaultFhirRequestContext()
                {
                    CorrelationId = Guid.NewGuid().ToString(),
                };

                fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

                var scopesClaim = new Claim(singleClaim, scopes);
                var claimsIdentity = new ClaimsIdentity(new List<Claim>() { scopesClaim, rolesClaim, fhirUserClaim });
                var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

                httpContext.User = expectedPrincipal;
                fhirRequestContext.Principal = expectedPrincipal;

                _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

                await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService);
                ValidateErrorResponse(
                    httpContext,
                    new[]
                    {
                        "_include and _revinclude are not supported",
                    },
                    true);
            }
        }

        [Theory]
        [MemberData(nameof(GetMalformedScopes))]
        public async Task GivenMalformedSmartScope_WhenInvoked_ThenBadRequestIsThrown(string scopes)
        {
            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            fhirConfiguration.Security.Enabled = true;
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.Enabled = true;
            await LoadRoles(authorizationConfiguration);

            var fhirUserClaim = new Claim(authorizationConfiguration.FhirUserClaim, "https://fhirServer/Patient/foo");
            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, "smartUser");

            using var stream = new MemoryStream();
            foreach (string singleClaim in authorizationConfiguration.ScopesClaim)
            {
                stream.SetLength(0);
                httpContext.Response.Body = stream;

                var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

                var fhirRequestContext = new DefaultFhirRequestContext()
                {
                    CorrelationId = Guid.NewGuid().ToString(),
                };

                fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

                var scopesClaim = new Claim(singleClaim, scopes);
                var claimsIdentity = new ClaimsIdentity(new List<Claim>() { scopesClaim, rolesClaim, fhirUserClaim });
                var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

                httpContext.User = expectedPrincipal;
                fhirRequestContext.Principal = expectedPrincipal;

                _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

                await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService);
                ValidateErrorResponse(httpContext);
            }
        }

        [Theory]
        [InlineData("patient/Observaton.rs")] // typo'd resource type
        [InlineData("patient/observation.rs")] // resource type casing must match the FHIR specification
        [InlineData("patient/Foobar.read")] // non-existent resource type
        [InlineData("user/NotAResource.cruds")] // non-existent resource type
        public async Task GivenSmartScopeWithUnknownResourceType_WhenInvoked_ThenBadRequestIsThrown(string scopes)
        {
            await AssertScopeThrowsBadRequest(scopes);
        }

        private async Task AssertScopeThrowsBadRequest(string scopes)
        {
            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            fhirConfiguration.Security.Enabled = true;
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.Enabled = true;
            await LoadRoles(authorizationConfiguration);

            var fhirUserClaim = new Claim(authorizationConfiguration.FhirUserClaim, "https://fhirServer/Patient/foo");
            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, "smartUser");

            using var stream = new MemoryStream();
            foreach (string singleClaim in authorizationConfiguration.ScopesClaim)
            {
                stream.SetLength(0);
                httpContext.Response.Body = stream;

                var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

                var fhirRequestContext = new DefaultFhirRequestContext()
                {
                    CorrelationId = Guid.NewGuid().ToString(),
                };

                fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

                var scopesClaim = new Claim(singleClaim, scopes);
                var claimsIdentity = new ClaimsIdentity(new List<Claim>() { scopesClaim, rolesClaim, fhirUserClaim });
                var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

                httpContext.User = expectedPrincipal;
                fhirRequestContext.Principal = expectedPrincipal;

                _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

                await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService);
                ValidateErrorResponse(httpContext);
            }
        }

        [Theory]
        [InlineData("smartUser", true, true)]
        [InlineData("globalAdmin", true, false)]
        [InlineData("smartUser", false, false)]
        [InlineData("globalAdmin", false, false)]
        public async Task GivenSmartDataAction_WhenInvoked_ThenApplyFineGrainedAccessControlIsSet(string role, bool securityEnabled, bool expectedApplyFineGrainedAccessControl)
        {
            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            var fhirRequestContext = new DefaultFhirRequestContext();

            fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            fhirConfiguration.Security.Enabled = securityEnabled;
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.Enabled = true;
            await LoadRoles(authorizationConfiguration);

            var fhirUserClaim = new Claim(authorizationConfiguration.FhirUserClaim, "https://fhirServer/Patient/foo");
            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, role);
            var claimsIdentity = new ClaimsIdentity(new List<Claim>() { rolesClaim, fhirUserClaim });
            var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

            httpContext.User = expectedPrincipal;
            fhirRequestContext.Principal = expectedPrincipal;

            _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

            await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService);

            Assert.Equal(expectedApplyFineGrainedAccessControl, fhirRequestContext.AccessControlContext.ApplyFineGrainedAccessControl);
        }

        [Fact]
        public async Task GivenSmartDataAction_WhenFhirUserNotProvided_ThenBadRequestExceptionThrown()
        {
            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            var fhirRequestContext = new DefaultFhirRequestContext()
            {
                CorrelationId = Guid.NewGuid().ToString(),
            };

            fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            fhirConfiguration.Security.Enabled = true;
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.Enabled = true;

            // ErrorOnMissingFhirUserClaim is true by default — no explicit assignment needed.
            await LoadRoles(authorizationConfiguration);

            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, "smartUser");
            var claimsIdentity = new ClaimsIdentity(new List<Claim>() { rolesClaim });
            var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

            httpContext.User = expectedPrincipal;
            fhirRequestContext.Principal = expectedPrincipal;

            _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

            using var stream = new MemoryStream();
            httpContext.Response.Body = stream;

            await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService);
            ValidateErrorResponse(httpContext);
        }

        [Fact]
        public async Task GivenOnlySystemScopes_WhenFhirUserNotProvided_ThenScopeRestrictionsAreApplied()
        {
            var fhirConfiguration = new FhirServerConfiguration();
            fhirConfiguration.Security.Enabled = true;
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.Enabled = true;
            await LoadRoles(authorizationConfiguration);

            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, "smartUser");

            foreach (string singleClaim in authorizationConfiguration.ScopesClaim)
            {
                var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
                var fhirRequestContext = new DefaultFhirRequestContext();
                fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

                HttpContext httpContext = new DefaultHttpContext();
                var scopesClaim = new Claim(singleClaim, "system/Patient.read");
                var claimsIdentity = new ClaimsIdentity(new List<Claim>() { scopesClaim, rolesClaim });
                var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

                httpContext.User = expectedPrincipal;
                fhirRequestContext.Principal = expectedPrincipal;

                _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

                await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService);

                Assert.Equal(
                    new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "system"),
                    Assert.Single(fhirRequestContext.AccessControlContext.AllowedResourceActions));
            }
        }

        [Fact]
        public async Task GivenAllDataActionExceptSmart_WhenScopesProvided_ThenScopeRestrictionsNotApplied()
        {
            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            var fhirRequestContext = new DefaultFhirRequestContext();

            fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            fhirConfiguration.Security.Enabled = true;
            authorizationConfiguration.Enabled = true;
            await LoadRoles(authorizationConfiguration);

            var fhirUserClaim = new Claim(authorizationConfiguration.FhirUserClaim, "https://fhirServer/Patient/foo");
            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, "globalAdmin");

            foreach (string singleClaim in authorizationConfiguration.ScopesClaim)
            {
                var scopesClaim = new Claim(singleClaim, "patient.Patient.read");
                var claimsIdentity = new ClaimsIdentity(new List<Claim>() { scopesClaim, rolesClaim, fhirUserClaim });
                var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

                httpContext.User = expectedPrincipal;
                fhirRequestContext.Principal = expectedPrincipal;

                _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

                await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService);
                Assert.Empty(fhirRequestContext.AccessControlContext.AllowedResourceActions);
            }
        }

        [Fact]
        public async Task GivenFhirUserInExtensionClaim_WhenRequestMade_ThenFhirUserIsSaved()
        {
            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            var fhirRequestContext = new DefaultFhirRequestContext();

            fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            fhirConfiguration.Security.Enabled = true;
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.EnableSmartWithoutAuth = true;
            await LoadRoles(authorizationConfiguration);

            var fhirUserClaim = new Claim(authorizationConfiguration.ExtensionFhirUserClaim, "https://fhirServer/Patient/foo");
            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, "smartUser");

            foreach (string singleClaim in authorizationConfiguration.ScopesClaim)
            {
                var scopesClaim = new Claim(singleClaim, "patient.Patient.read");
                var claimsIdentity = new ClaimsIdentity(new List<Claim>() { scopesClaim, rolesClaim, fhirUserClaim });
                var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

                httpContext.User = expectedPrincipal;
                fhirRequestContext.Principal = expectedPrincipal;

                _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

                await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService);

                Assert.Equal(new Uri("https://fhirServer/Patient/foo"), fhirRequestContext.AccessControlContext.FhirUserClaim);
            }
        }

        [Fact]
        public async Task GivenFhirUserAndExtensionFhirUserClaimsBothExist_WhenRequestMade_ThenFhirUserClaimIsUsed()
        {
            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            var fhirRequestContext = new DefaultFhirRequestContext();

            fhirRequestContextAccessor.RequestContext.Returns(fhirRequestContext);

            HttpContext httpContext = new DefaultHttpContext();

            var fhirConfiguration = new FhirServerConfiguration();
            fhirConfiguration.Security.Enabled = true;
            var authorizationConfiguration = fhirConfiguration.Security.Authorization;
            authorizationConfiguration.EnableSmartWithoutAuth = true;
            await LoadRoles(authorizationConfiguration);

            var fhirUserClaim = new Claim(authorizationConfiguration.ExtensionFhirUserClaim, "https://fhirServer/Patient/foo1");
            var extensionFhirUserClaim = new Claim(authorizationConfiguration.ExtensionFhirUserClaim, "https://fhirServer/Patient/foo2");
            var rolesClaim = new Claim(authorizationConfiguration.RolesClaim, "smartUser");

            foreach (string singleClaim in authorizationConfiguration.ScopesClaim)
            {
                var scopesClaim = new Claim(singleClaim, "patient.Patient.read");
                var claimsIdentity = new ClaimsIdentity(new List<Claim>() { scopesClaim, rolesClaim, fhirUserClaim, extensionFhirUserClaim });
                var expectedPrincipal = new ClaimsPrincipal(claimsIdentity);

                httpContext.User = expectedPrincipal;
                fhirRequestContext.Principal = expectedPrincipal;

                _authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, fhirRequestContextAccessor);

                await _smartClinicalScopesMiddleware.Invoke(httpContext, fhirRequestContextAccessor, Options.Create(fhirConfiguration.Security), _authorizationService);

                Assert.Equal(new Uri("https://fhirServer/Patient/foo1"), fhirRequestContext.AccessControlContext.FhirUserClaim);
            }
        }

        [Theory]
        [InlineData("raw_scope", "patient/Patient.read patient%2FObservation.read", new[] { "patient/Patient.read", " patient/Observation.read" })]
        [InlineData("raw_scope", "patient/Patient.read\tpatient%2FObservation.read", new[] { "patient/Patient.read", "\tpatient/Observation.read" })]
        [InlineData("scope", "patient/Patient.read patient%2FObservation.read", new[] { " patient/Patient.read", " patient/Observation.read" })]
        [InlineData("scope", "patient%2FObservation.rs?code=http:%2F%2Floinc.org|4548-4", new[] { " patient/Observation.rs?code=http:%2F%2Floinc.org|4548-4" })]
        public async Task GivenSmartScopes_WhenInvoked_ThenClinicalScopesKeepTheOriginalSeparators(string scopeClaim, string scopes, string[] expectedClinicalScopes)
        {
            var claims = new List<Claim>
            {
                new Claim(scopeClaim, scopes),
                new Claim("fhirUser", "https://fhirServer/Patient/foo"),
            };

            var (accessControlContext, nextCalled) = await InvokeWithSmartClaimsAsync(new DefaultHttpContext(), claims);

            Assert.True(nextCalled);
            Assert.Equal(expectedClinicalScopes, accessControlContext.ClinicalScopes);
        }

        [Theory]
        [InlineData("patient.all.read", "https://fhirServer/Practitioner/doctor", "scope")]
        [InlineData("patient/*.rs", "Practitioner/doctor", "scope")]
        [InlineData("patient/Observation.rs", "Practitioner/doctor", "raw_scope")]
        [InlineData("patient/Observation.read", "Practitioner/doctor", "roles")]
        [InlineData("patient/Patient.read", "https://fhirServer/Patient/other-patient", "scope")]
        [InlineData("patient/Patient.rs", null, "scope")]
        public async Task GivenPatientScopeAndPatientClaim_WhenInvoked_ThenSelectedPatientCompartmentIsUsed(string scope, string fhirUser, string scopeClaim)
        {
            var claims = new List<Claim>
            {
                new Claim(scopeClaim, scope),
                new Claim("patient", "selected-patient"),
            };
            if (fhirUser != null)
            {
                claims.Add(new Claim("fhirUser", fhirUser));
            }

            var httpContext = new DefaultHttpContext();

            var (accessControlContext, nextCalled) = await InvokeWithSmartClaimsAsync(httpContext, claims);

            Assert.True(nextCalled);
            Assert.Equal("Patient", accessControlContext.CompartmentResourceType);
            Assert.Equal("selected-patient", accessControlContext.CompartmentId);
            Assert.True(accessControlContext.ApplyFineGrainedAccessControl);
            Assert.Single(accessControlContext.AllowedResourceActions);
        }

        [Theory]
        [InlineData(null, "Practitioner/doctor")]
        [InlineData(null, null)]
        [InlineData(null, "Organization/organization")]
        [InlineData(null, "invalid")]
        [InlineData("", "Patient/legacy-patient")]
        [InlineData(" ", "Patient/legacy-patient")]
        [InlineData("Patient/selected-patient", "Patient/legacy-patient")]
        [InlineData("https://fhirServer/Patient/selected-patient", "Patient/legacy-patient")]
        [InlineData("selected-patient/other", "Patient/legacy-patient")]
        [InlineData("selected-patient?x=y", "Patient/legacy-patient")]
        [InlineData("selected-patient,other", "Patient/legacy-patient")]
        [InlineData("selected%2Dpatient", "Patient/legacy-patient")]
        public async Task GivenPatientScopeWithoutValidPatientContext_WhenInvoked_ThenRequestIsRejected(string patient, string fhirUser)
        {
            foreach (bool errorOnMissingFhirUserClaim in new[] { true, false })
            {
                var claims = new List<Claim>
                {
                    new Claim("scope", "patient.all.read"),
                };
                if (patient != null)
                {
                    claims.Add(new Claim("patient", patient));
                }

                if (fhirUser != null)
                {
                    claims.Add(new Claim("fhirUser", fhirUser));
                }

                var httpContext = new DefaultHttpContext();
                using var stream = new MemoryStream();
                httpContext.Response.Body = stream;
                httpContext.Request.QueryString = new QueryString("?patient=selected-patient");
                httpContext.Request.Headers["patient"] = "selected-patient";

                var (_, nextCalled) = await InvokeWithSmartClaimsAsync(httpContext, claims, errorOnMissingFhirUserClaim);

                Assert.False(nextCalled);
                ValidateErrorResponse(httpContext);
            }
        }

        [Theory]
        [InlineData("selected-patient")]
        [InlineData("other-patient")]
        [InlineData("")]
        public async Task GivenMultiplePatientClaims_WhenInvoked_ThenRequestIsRejected(string secondPatient)
        {
            var claims = new[]
            {
                new Claim("scope", "patient/*.rs"),
                new Claim("patient", "selected-patient"),
                new Claim("patient", secondPatient),
                new Claim("fhirUser", "Patient/legacy-patient"),
            };
            var httpContext = new DefaultHttpContext();
            using var stream = new MemoryStream();
            httpContext.Response.Body = stream;

            var (_, nextCalled) = await InvokeWithSmartClaimsAsync(httpContext, claims);

            Assert.False(nextCalled);
            ValidateErrorResponse(httpContext, new[] { Api.Resources.SmartPatientContextInvalid });
        }

        [Theory]
        [InlineData("patient/Patient.read", "fhirUser")]
        [InlineData("patient/Patient.rs", "extension_fhirUser")]
        public async Task GivenLegacyPatientScopeWithoutPatientClaim_WhenInvoked_ThenPatientFhirUserRemainsSupported(string scope, string fhirUserClaim)
        {
            var claims = new[]
            {
                new Claim("scope", scope),
                new Claim(fhirUserClaim, "https://fhirServer/Patient/legacy-patient"),
            };
            var httpContext = new DefaultHttpContext();

            var (accessControlContext, nextCalled) = await InvokeWithSmartClaimsAsync(httpContext, claims);

            Assert.True(nextCalled);
            Assert.Equal((int)HttpStatusCode.OK, httpContext.Response.StatusCode);
            Assert.Equal("Patient", accessControlContext.CompartmentResourceType);
            Assert.Equal("legacy-patient", accessControlContext.CompartmentId);
        }

        [Theory]
        [InlineData("user/Patient.read", "Practitioner", "doctor")]
        [InlineData("system/Patient.read", null, null)]
        public async Task GivenNonPatientScopeWithPatientClaim_WhenInvoked_ThenExistingCompartmentBehaviorIsPreserved(string scope, string expectedType, string expectedId)
        {
            var claims = new[]
            {
                new Claim("scope", scope),
                new Claim("fhirUser", "Practitioner/doctor"),
                new Claim("patient", "Patient/invalid-context"),
            };
            var httpContext = new DefaultHttpContext();

            var (accessControlContext, nextCalled) = await InvokeWithSmartClaimsAsync(httpContext, claims);

            Assert.True(nextCalled);
            Assert.Equal((int)HttpStatusCode.OK, httpContext.Response.StatusCode);
            Assert.Equal(expectedType, accessControlContext.CompartmentResourceType);
            Assert.Equal(expectedId, accessControlContext.CompartmentId);
        }

        [Theory]
        [InlineData(64, false, true)]
        [InlineData(65, false, false)]
        [InlineData(128, true, true)]
        [InlineData(129, true, false)]
        public async Task GivenPatientClaim_WhenInvoked_ThenResourceIdLengthPolicyIsEnforced(int length, bool useLongResourceIds, bool expectedSuccess)
        {
            string patient = new string('a', length);
            var claims = new[]
            {
                new Claim("scope", "patient/*.rs"),
                new Claim("patient", patient),
            };
            var httpContext = new DefaultHttpContext();
            using var stream = new MemoryStream();
            httpContext.Response.Body = stream;

            var (accessControlContext, nextCalled) = await InvokeWithSmartClaimsAsync(httpContext, claims, useLongResourceIds: useLongResourceIds);

            Assert.Equal(expectedSuccess, nextCalled);
            if (expectedSuccess)
            {
                Assert.Equal("Patient", accessControlContext.CompartmentResourceType);
                Assert.Equal(patient, accessControlContext.CompartmentId);
            }
            else
            {
                ValidateErrorResponse(httpContext, new[] { Api.Resources.SmartPatientContextInvalid });
            }
        }

        public static IEnumerable<object[]> GetTestScopesAndRoles()
        {
            yield return new object[]
            {
                "patient/Patient.read",
                "patient/Observation.read",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "patient"),
                    new ScopeRestriction("Observation", DataActions.Read | DataActions.Export | DataActions.Search, "patient"),
                },
            };
            yield return new object[]
            {
                "user.Patient.read",
                "user.Observation.write",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "user"),
                    new ScopeRestriction("Observation", DataActions.Write | DataActions.Create | DataActions.Delete | DataActions.Update, "user"),
                },
            };
            yield return new object[]
            {
                "patient$Patient.read",
                "practitioner/Observation.write",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "patient"),
                },
            };
            yield return new object[]
            {
                "patient$Patient.rd",
                "practitioner/Observation.wr",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.ReadById | DataActions.Delete, "patient"),
                },
            };
        }

        public static IEnumerable<object[]> GetTestScopes()
        {
            yield return new object[] { "patient/Patient.read", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "patient") } };
            yield return new object[]
            {
                "patient/Patient.read patient/Observation.read",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "patient"),
                    new ScopeRestriction("Observation", DataActions.Read | DataActions.Export | DataActions.Search, "patient"),
                },
            };
            yield return new object[]
            {
                "user.Patient.read user.Observation.write",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "user"),
                    new ScopeRestriction("Observation", DataActions.Write | DataActions.Create | DataActions.Update | DataActions.Delete, "user"),
                },
            };

            yield return new object[]
            {
                "user.VisionPrescription.write user.all.read",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("VisionPrescription", DataActions.Write | DataActions.Create | DataActions.Update | DataActions.Delete, "user"),
                    new ScopeRestriction(KnownResourceTypes.All, DataActions.Read | DataActions.Export | DataActions.Search, "user"),
                },
            };

            yield return new object[]
            {
                "user/*.*",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction(KnownResourceTypes.All, DataActions.Read | DataActions.Search | DataActions.Write | DataActions.Export | DataActions.Create | DataActions.Update | DataActions.Delete, "user"),
                },
            };

            yield return new object[] { "user/Encounter.*", new List<ScopeRestriction>() { new ScopeRestriction("Encounter", DataActions.Read | DataActions.Search | DataActions.Write | DataActions.Export | DataActions.Create | DataActions.Update | DataActions.Delete, "user") } };
            yield return new object[] { "user/all.*", new List<ScopeRestriction>() { new ScopeRestriction(KnownResourceTypes.All, DataActions.Read | DataActions.Search | DataActions.Write | DataActions.Export | DataActions.Create | DataActions.Update | DataActions.Delete, "user") } };
            yield return new object[] { "user/all.all", new List<ScopeRestriction>() { new ScopeRestriction(KnownResourceTypes.All, DataActions.Read | DataActions.Search | DataActions.Write | DataActions.Export | DataActions.Create | DataActions.Update | DataActions.Delete, "user") } };
            yield return new object[] { "system.all.all", new List<ScopeRestriction>() { new ScopeRestriction(KnownResourceTypes.All, DataActions.Read | DataActions.Search | DataActions.Write | DataActions.Export | DataActions.Create | DataActions.Update | DataActions.Delete, "system") } };
            yield return new object[] { "patient.Patient.read", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "patient") } };
            yield return new object[] { "patient.Patient.all", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Read | DataActions.Search | DataActions.Write | DataActions.Export | DataActions.Create | DataActions.Update | DataActions.Delete, "patient") } };
            yield return new object[] { "patient.*.read", new List<ScopeRestriction>() { new ScopeRestriction(KnownResourceTypes.All, DataActions.Read | DataActions.Export | DataActions.Search, "patient") } };
            yield return new object[] { "patient.all.read", new List<ScopeRestriction>() { new ScopeRestriction(KnownResourceTypes.All, DataActions.Read | DataActions.Export | DataActions.Search, "patient") } };
            yield return new object[]
            {
                "patient$Patient.read practitioner/Observation.write",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "patient"),
                },
            };
            yield return new object[]
            {
                "patient$Patient.rd practitioner/Observation.wr",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.ReadById | DataActions.Delete, "patient"),
                },
            };
            yield return new object[]
            {
                "patient/Patient.read launch/patient patient/Observation.read offline_access openid patient/Encounter.* fhirUser",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "patient"),
                    new ScopeRestriction("Observation", DataActions.Read | DataActions.Export | DataActions.Search, "patient"),
                    new ScopeRestriction("Encounter", DataActions.Read | DataActions.Search | DataActions.Write | DataActions.Export | DataActions.Create | DataActions.Update | DataActions.Delete, "patient"),
                },
            };

            // SMART v2 scope format tests
            yield return new object[] { "patient/Patient.rs", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient") } };
            yield return new object[] { "patient/Patient.r", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.ReadById, "patient") } };
            yield return new object[] { "patient/Patient.s", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Search | DataActions.Export, "patient") } };
            yield return new object[] { "patient/Patient.c", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Create, "patient") } };
            yield return new object[] { "patient/all.c", new List<ScopeRestriction>() { new ScopeRestriction(KnownResourceTypes.All, DataActions.Create, "patient") } };
            yield return new object[] { "patient.all.c", new List<ScopeRestriction>() { new ScopeRestriction(KnownResourceTypes.All, DataActions.Create, "patient") } };
            yield return new object[] { "patient/Patient.u", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Update, "patient") } };
            yield return new object[] { "patient/Patient.d", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Delete, "patient") } };
            yield return new object[] { "patient/Patient.cruds", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Create | DataActions.Update | DataActions.Delete | DataActions.ReadById | DataActions.Search | DataActions.Export, "patient") } };
            yield return new object[] { "user/*.rs", new List<ScopeRestriction>() { new ScopeRestriction(KnownResourceTypes.All, DataActions.ReadById | DataActions.Search | DataActions.Export, "user") } };
            yield return new object[]
            {
                "patient/Patient.rs patient/Observation.cud",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient"),
                    new ScopeRestriction("Observation", DataActions.Create | DataActions.Update | DataActions.Delete, "patient"),
                },
            };

            // Test v1 vs v2 behavior: v1 .read includes search, v2 .r does not include search
            yield return new object[] { "patient/Patient.read", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "patient") } };
            yield return new object[]
            {
                "patient/Patient.s patient/Observation.r",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Export | DataActions.Search, "patient"),
                    new ScopeRestriction("Observation", DataActions.ReadById, "patient"),
                },
            };

            // Test v1 vs v2 write behavior: v1 .write includes all write operations, v2 granular permissions
            yield return new object[] { "patient/Patient.write", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Write | DataActions.Create | DataActions.Update | DataActions.Delete, "patient") } };
            yield return new object[]
            {
                "patient/Patient.c patient/Observation.cu",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Create, "patient"),
                    new ScopeRestriction("Observation", DataActions.Create | DataActions.Update, "patient"),
                },
            };

            // SMART v2 granular scopes with search parameters

            yield return new object[]
            {
                "patient/Patient.rs?name=john",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("name", "john")),
                },
            };

            yield return new object[]
            {
                "patient/Observation.s?code=44501",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Observation", DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("code", "44501")),
                },
            };

            // Multiple search parameters
            yield return new object[]
            {
                "patient/Patient.rs?name=john&gender=male",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("name", "john").Add("gender", "male")),
                },
            };

            // Multiple scopes with search parameters
            yield return new object[]
            {
                "patient/Patient.rs?name=john patient/Observation.s?code=44501&status=final",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("name", "john")),
                    new ScopeRestriction("Observation", DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("code", "44501").Add("status", "final")),
                },
            };

            // Complex example with all SMART v2 granular permissions and search parameters
            yield return new object[]
            {
                "user/Patient.cruds?name=Smith&birthdate=ge2000 user/Observation.rs?category=vital-signs",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Create | DataActions.ReadById | DataActions.Update | DataActions.Delete | DataActions.Search | DataActions.Export, "user", new Hl7.Fhir.Rest.SearchParams("name", "Smith").Add("birthdate", "ge2000")),
                    new ScopeRestriction("Observation", DataActions.ReadById | DataActions.Search | DataActions.Export, "user", new Hl7.Fhir.Rest.SearchParams("category", "vital-signs")),
                },
            };

            // System scope with search parameters
            yield return new object[]
            {
                "system/Patient.rs?active=true",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.ReadById | DataActions.Search | DataActions.Export, "system", new Hl7.Fhir.Rest.SearchParams("active", "true")),
                },
            };

            // Base parameter names that collide with modifier literals must NOT be rejected (they are valid search params, not modifiers)
            yield return new object[]
            {
                "patient/Patient.rs?identifier=http://example.com|123",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("identifier", "http://example.com|123")),
                },
            };

            // URL-encoded scopes with %2f (for Azure Entra ID compatibility)
            // Basic test with %2f in scope separator
            yield return new object[] { "patient%2fPatient.read", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "patient") } };

            // Multiple scopes with %2f
            yield return new object[]
            {
                "patient%2fPatient.read patient%2fObservation.read",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "patient"),
                    new ScopeRestriction("Observation", DataActions.Read | DataActions.Export | DataActions.Search, "patient"),
                },
            };

            // V2 format with %2f
            yield return new object[] { "patient%2fPatient.rs", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient") } };

            // V2 format with multiple permissions and %2f
            yield return new object[] { "patient%2fPatient.cruds", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Create | DataActions.Update | DataActions.Delete | DataActions.ReadById | DataActions.Search | DataActions.Export, "patient") } };

            // Wildcard with %2f
            yield return new object[]
            {
                "user%2f*.*",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction(KnownResourceTypes.All, DataActions.Read | DataActions.Search | DataActions.Write | DataActions.Export | DataActions.Create | DataActions.Update | DataActions.Delete, "user"),
                },
            };

            // System scope with %2f
            yield return new object[] { "system%2fPatient.all", new List<ScopeRestriction>() { new ScopeRestriction("Patient", DataActions.Read | DataActions.Search | DataActions.Write | DataActions.Export | DataActions.Create | DataActions.Update | DataActions.Delete, "system") } };

            // %2f with search parameters (granular scopes)
            yield return new object[]
            {
                "patient%2fObservation.rs?code=44501",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Observation", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("code", "44501")),
                },
            };

            // %2f with multiple search parameters
            yield return new object[]
            {
                "patient%2fPatient.rs?name=john&gender=male",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("name", "john").Add("gender", "male")),
                },
            };

            // %2f with URL-encoded parameters (http URLs with encoded forward slashes in search params)
            yield return new object[]
            {
                "patient%2fObservation.rs?code=http:%2f%2floinc.org|4454-1",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Observation", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("code", "http://loinc.org|4454-1")),
                },
            };

            // Some scopes with %2f, some with regular / separator
            yield return new object[]
            {
                "patient%2fPatient.read patient/Observation.write",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "patient"),
                    new ScopeRestriction("Observation", DataActions.Write | DataActions.Create | DataActions.Update | DataActions.Delete, "patient"),
                },
            };

            // Multiple scopes with different separators and %2f in search parameters
            yield return new object[]
            {
                "patient%2fObservation.s?code=http:%2f%2floinc.org|4454-1&status=final patient/Patient.rs?name=john",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Observation", DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("code", "http://loinc.org|4454-1").Add("status", "final")),
                    new ScopeRestriction("Patient", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("name", "john")),
                },
            };

            // V2 granular with %2f
            yield return new object[]
            {
                "user%2fPatient.cud user%2fObservation.rs",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Create | DataActions.Update | DataActions.Delete, "user"),
                    new ScopeRestriction("Observation", DataActions.ReadById | DataActions.Search | DataActions.Export, "user"),
                },
            };

            // Complex example with %2f in scope separator and %2f in URL search parameters
            yield return new object[]
            {
                "user%2fPatient.cruds?name=Smith&birthdate=ge2000 user%2fObservation.rs?category=vital-signs&code=http:%2f%2floinc.org|85354-9",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Create | DataActions.ReadById | DataActions.Update | DataActions.Delete | DataActions.Search | DataActions.Export, "user", new Hl7.Fhir.Rest.SearchParams("name", "Smith").Add("birthdate", "ge2000")),
                    new ScopeRestriction("Observation", DataActions.ReadById | DataActions.Search | DataActions.Export, "user", new Hl7.Fhir.Rest.SearchParams("category", "vital-signs").Add("code", "http://loinc.org|85354-9")),
                },
            };

            // MSRC 165509: search-parameter values are decoded only after the query is split on the raw '&' and '='
            // delimiters. A raw '&' separates constraints (ANDed); an encoded '%26' or '%3D' is a literal character
            // inside the value, matching how FHIR search reads GET /Observation?identifier=ABC%26status%3Dfinal.

            // Raw '&' separates two constraints
            yield return new object[]
            {
                "patient%2fObservation.read?identifier=ABC&status=final",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Observation", DataActions.Read | DataActions.Export | DataActions.Search, "patient", new Hl7.Fhir.Rest.SearchParams("identifier", "ABC").Add("status", "final")),
                },
            };

            // Encoded '%26' and '%3D' stay inside one literal identifier value (the MSRC 165509 scope)
            yield return new object[]
            {
                "patient%2fObservation.read?identifier=ABC%26status%3Dfinal",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Observation", DataActions.Read | DataActions.Export | DataActions.Search, "patient", new Hl7.Fhir.Rest.SearchParams("identifier", "ABC&status=final")),
                },
            };

            // Same scope with a raw '/' and upper-case escapes
            yield return new object[]
            {
                "patient/Observation.rs?identifier=ABC%26STATUS%3DFINAL",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Observation", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("identifier", "ABC&STATUS=FINAL")),
                },
            };

            // A literal '&' value combined with a second constraint separated by a raw '&'
            yield return new object[]
            {
                "patient%2fObservation.rs?identifier=ABC%26status%3Dfinal&status=final",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Observation", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("identifier", "ABC&status=final").Add("status", "final")),
                },
            };

            // Literal '=' only
            yield return new object[]
            {
                "patient%2fObservation.rs?identifier=a%3Db",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Observation", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("identifier", "a=b")),
                },
            };

            // Literal '&' only, with a system
            yield return new object[]
            {
                "user%2fObservation.rs?identifier=http:%2f%2fexample.org|A%26B",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Observation", DataActions.ReadById | DataActions.Search | DataActions.Export, "user", new Hl7.Fhir.Rest.SearchParams("identifier", "http://example.org|A&B")),
                },
            };

            // Encoded '?' and '=' are a literal value, unlike the malformed raw "identifier=x?mrn=12345"
            yield return new object[]
            {
                "patient%2fObservation.rs?identifier=x%3Fmrn%3D12345",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Observation", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("identifier", "x?mrn=12345")),
                },
            };

            // Values are decoded exactly once: "%2526" is the literal text "%26", not '&'
            yield return new object[]
            {
                "patient%2fObservation.rs?identifier=ABC%2526status%253Dfinal",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Observation", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("identifier", "ABC%26status%3Dfinal")),
                },
            };

            // An encoded space inside a value stays in the value and does not start a second scope
            yield return new object[]
            {
                "patient%2fObservation.rs?code=x%20patient%2fPatient.read",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Observation", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("code", "x patient/Patient.read")),
                },
            };

            // Literal values in one scope do not leak into the next scope
            yield return new object[]
            {
                "patient%2fObservation.rs?identifier=ABC%26status%3Dfinal patient%2fPatient.rs?name=john&gender=male",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Observation", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("identifier", "ABC&status=final")),
                    new ScopeRestriction("Patient", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("name", "john").Add("gender", "male")),
                },
            };

            // Encoded characters in a key are decoded after the split, as they were when the whole scope was decoded
            yield return new object[]
            {
                "patient%2fObservation.rs?subject%3APatient=Patient%2F123",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Observation", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("subject:Patient", "Patient/123")),
                },
            };

            // Encoded whitespace at either end of a scope is trimmed, as before
            yield return new object[]
            {
                "%20patient%2fPatient.read%20",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "patient"),
                },
            };

            // Encoded whitespace between the prefix and a raw '?' no longer drops the constraint
            yield return new object[]
            {
                "patient%2fObservation.rs%20?code=44501",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Observation", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("code", "44501")),
                },
            };

            // Tabs and line breaks still separate scopes
            yield return new object[]
            {
                "patient/Patient.read\tpatient%2fObservation.read\r\npatient/Encounter.read",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "patient"),
                    new ScopeRestriction("Observation", DataActions.Read | DataActions.Export | DataActions.Search, "patient"),
                    new ScopeRestriction("Encounter", DataActions.Read | DataActions.Export | DataActions.Search, "patient"),
                },
            };

            // Non-clinical scopes with encoded delimiters are ignored, as before
            yield return new object[]
            {
                "openid launch%3Fpatient%3D123 api%3A%2F%2Fapp%2Ffhir patient%2fPatient.read",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Patient", DataActions.Read | DataActions.Export | DataActions.Search, "patient"),
                },
            };

            // Other escapes in values decode exactly as before: %2C is the FHIR OR separator, invalid escapes are kept
            yield return new object[]
            {
                "patient%2fObservation.rs?code=a%2Cb&status=50%zz",
                new List<ScopeRestriction>()
                {
                    new ScopeRestriction("Observation", DataActions.ReadById | DataActions.Search | DataActions.Export, "patient", new Hl7.Fhir.Rest.SearchParams("code", "a,b").Add("status", "50%zz")),
                },
            };
        }

        public static IEnumerable<object[]> GetMixedSmartScopeVersions()
        {
            yield return new object[] { "patient/Patient.read patient/Observation.r" };
            yield return new object[] { "patient.Patient.read patient.Observation.cr" };
            yield return new object[] { "patient$Patient.rd patient/Observation.write" };
            yield return new object[] { "patient$Patient.read patient/Observation.cr" };
            yield return new object[] { "patient/Patient.read launch/patient patient/Observation.read offline_access openid patient/Encounter.r fhirUser" };
            yield return new object[] { "patient/Patient.rs patient/Observation.cud patient/Encounter.write" };
            yield return new object[] { "patient/Patient.read patient/Observation.c" };
            yield return new object[] { "patient/Patient.all patient/Observation.r" };
            yield return new object[] { "patient/Patient.write patient/Observation.u" };
            yield return new object[] { "patient/Patient.read patient/Observation.d" };
            yield return new object[] { "patient/Patient.read patient/Patient.cruds" };
            yield return new object[] { "system/Patient.write system/Patient.r" };
            yield return new object[] { "patient/Patient.* patient/Observation.d" };
            yield return new object[] { "patient/Patient.all patient/Patient.cruds" };
            yield return new object[] { "system/Patient.all system/Patient.r" };
            yield return new object[] { "system/Patient.* system/Patient.r" };
        }

        public static IEnumerable<object[]> GetMixedSmartScopeContexts()
        {
            yield return new object[] { "patient/Patient.read user/Observation.read" };
            yield return new object[] { "user/Patient.read patient/Observation.read" };
            yield return new object[] { "patient/Patient.read system/Observation.read" };
            yield return new object[] { "system/Patient.read patient/Observation.read" };
            yield return new object[] { "user/Patient.read system/Observation.read" };
            yield return new object[] { "system/Patient.read user/Observation.read" };
            yield return new object[] { "patient/Patient.read user/Observation.read system/Encounter.read" };
            yield return new object[] { "patient%2fPatient.read system/Observation.read" };
        }

        public static IEnumerable<object[]> GetScopesWithModifiers()
        {
            // Modifiers on search parameter names
            yield return new object[] { "patient/Patient.rs?gender:not=male" };
            yield return new object[] { "patient/Patient.rs?gender:missing=true" };
            yield return new object[] { "patient/Patient.rs?name:exact=Smith" };
            yield return new object[] { "patient/Patient.rs?name:contains=mit" };
            yield return new object[] { "user/Condition.rs?category:in=http://hl7.org/fhir/ValueSet/condition-category" };
            yield return new object[] { "user/Condition.rs?category:not-in=http://hl7.org/fhir/ValueSet/condition-category" };
            yield return new object[] { "user/Condition.rs?code:text=diabetes" };
            yield return new object[] { "patient/Observation.rs?code:above=http://loinc.org|LP29693-6" };
            yield return new object[] { "patient/Observation.rs?code:below=http://loinc.org|LP29693-6" };
            yield return new object[] { "patient/Observation.rs?subject:identifier=http://example.com|123" };
            yield return new object[] { "patient/Observation.rs?value-quantity:ofType=http://unitsofmeasure.org|mg" };

            // Include/revinclude modifiers (_include:iterate is R4, _revinclude:recurse is STU3)
            yield return new object[] { "patient/Patient.rs?_include:iterate=Observation:subject" };
            yield return new object[] { "patient/Patient.rs?_revinclude:recurse=Observation:patient" };

            // Modifier combined with normal param
            yield return new object[] { "user/Condition.rs?category:in=http://hl7.org/fhir/ValueSet/x&status=active" };

            // Modifier hidden behind an encoded ':' in the key (keys are decoded before the checks)
            yield return new object[] { "patient/Observation.rs?code%3Anot=x" };
            yield return new object[] { "patient%2fPatient.rs?gender%3amissing=true" };
        }

        public static IEnumerable<object[]> GetScopesWithChainedSearch()
        {
            // Forward chaining (dot in the key), with and without an additional modifier
            yield return new object[] { "patient/Observation.rs?subject.name=Smith" };
            yield return new object[] { "patient/Observation.rs?subject:Patient.name=Smith" };
            yield return new object[] { "patient/Observation.rs?subject:Patient.name:exact=Smith" };
            yield return new object[] { "user/Observation.rs?patient.birthdate:missing=true" };
            yield return new object[] { "user/Patient.rs?general-practitioner:Practitioner.name:contains=Smith" };

            // Chained search combined with a normal param
            yield return new object[] { "patient/Observation.rs?subject.name=Smith&status=final" };

            // Reverse chaining (_has), with and without an additional modifier
            yield return new object[] { "user/Patient.rs?_has:Observation:patient:code=http://loinc.org|1234-5" };
            yield return new object[] { "user/Patient.rs?_has:Observation:patient:code:in=http://loinc.org|1234-5" };

            // Chained search hidden behind an encoded '.' or ':' in the key
            yield return new object[] { "patient/Observation.rs?subject%2Ename=Smith" };
            yield return new object[] { "user/Patient.rs?_has%3AObservation%3Apatient%3Acode=http://loinc.org|1234-5" };
        }

        public static IEnumerable<object[]> GetScopesWithIncludes()
        {
            // _include / _revinclude result parameters (plain form) are not enforceable scope constraints
            yield return new object[] { "patient/Observation.rs?_include=Observation:subject" };
            yield return new object[] { "patient/Patient.rs?_revinclude=Observation:subject" };

            // Combined with a normal search parameter
            yield return new object[] { "patient/Observation.rs?code=http://loinc.org|55233-1&_include=Observation:subject" };
            yield return new object[] { "patient/Patient.rs?name=SMARTGivenName1&_revinclude=Observation:subject" };

            // Case-insensitive key
            yield return new object[] { "patient/Observation.rs?_INCLUDE=Observation:subject" };

            // _include hidden behind an encoded '_' in the key
            yield return new object[] { "patient/Observation.rs?%5Finclude=Observation:subject" };
        }

        public static IEnumerable<object[]> GetMalformedScopes()
        {
            // Trailing junk on an otherwise valid scope token: the regex finds the valid prefix but the
            // token is not fully consumed, so the scope must be rejected rather than silently enforcing
            // the truncated prefix (over-broad / fail-open).
            yield return new object[] { "patient/Observation.read-only" };   // "-only" not consumed
            yield return new object[] { "patient/Observation.rs?active" };    // "?active" has no '=', not consumed
            yield return new object[] { "patient/Observation.rsXYZ" };        // "XYZ" not consumed
            yield return new object[] { "patient/Observation.rssszz" };       // matches "rsss", trailing "zz" not consumed

            // Malformed search-parameter constraints with an extra '=' that previously bypassed the
            // modifier/chained checks and were silently dropped (fail-open).
            yield return new object[] { "patient/Observation.rs?code:exact=a=b" };          // modifier hidden behind extra '='
            yield return new object[] { "patient/Observation.rs?identifier=x?mrn=12345" };  // extra '=' in value

            // v2 access level with duplicate permission letters (e.g. "rrrrrs"): the regex [cruds]+ accepts
            // repeats, but a duplicated letter is malformed and must be rejected.
            yield return new object[] { "patient/Observation.rrrrrs" };     // duplicate 'r'
            yield return new object[] { "patient/Observation.rss" };        // duplicate 's'
            yield return new object[] { "patient/Observation.rrrrssssss" }; // multiple duplicates
            yield return new object[] { "user/Patient.crudss" };            // duplicate 's' after a full set
            yield return new object[] { "user/Patient.crudsc" };            // duplicate 'c' wrapping a full set

            // MSRC 165509: delimiters must be raw. Encoded delimiters outside a search-parameter value are rejected
            // rather than decoded into extra constraints or extra scopes.
            yield return new object[] { "patient%2fObservation.rs%3Fcode%3D44501" };              // fully encoded scope: '?' must be raw
            yield return new object[] { "patient%2fObservation.rs%26code%3D44501" };              // encoded '&' before the query
            yield return new object[] { "patient%2fObservation.read%20patient%2fPatient.read" };  // one scope value expanding into two scopes
            yield return new object[] { "patient/Observation.rs?identifier=ABC%26status=final" }; // raw '=' left in the value after the split
            yield return new object[] { "patient/Observation.rs?identifier%3DABC" };              // encoded '=' leaves no raw name/value separator
            yield return new object[] { "patient/Observation.rs?a%26b=c" };                       // decoded key contains '&'
            yield return new object[] { "patient/Observation.rs?code%20x=1" };                    // decoded key contains a space
            yield return new object[] { "patient/Observation.rs?%3F=1" };                         // decoded key contains '?'
            yield return new object[] { "patient%2fPatient.read%20openid" };                      // encoded space inside one scope value

            // Values that decode to whitespace only can't be enforced as search constraints
            yield return new object[] { "patient/Observation.rs?identifier=%20" };
            yield return new object[] { "patient/Observation.rs?identifier=%09%0A" };
            yield return new object[] { "patient%2fObservation.rs?code=44501&identifier=%C2%A0" }; // no-break space, after a valid constraint
        }

        private async Task<(AccessControlContext AccessControlContext, bool NextCalled)> InvokeWithSmartClaimsAsync(
            HttpContext httpContext,
            IEnumerable<Claim> claims,
            bool errorOnMissingFhirUserClaim = true,
            bool useLongResourceIds = false)
        {
            var securityConfiguration = new SecurityConfiguration { Enabled = true };
            var authorizationConfiguration = securityConfiguration.Authorization;
            authorizationConfiguration.Enabled = true;
            authorizationConfiguration.ErrorOnMissingFhirUserClaim = errorOnMissingFhirUserClaim;
            await LoadRoles(authorizationConfiguration);

            var identity = new ClaimsIdentity(claims);
            identity.AddClaim(new Claim(authorizationConfiguration.RolesClaim, "smartUser"));
            var principal = new ClaimsPrincipal(identity);
            var requestContext = new DefaultFhirRequestContext { Principal = principal, CorrelationId = Guid.NewGuid().ToString() };
            var accessor = new FhirRequestContextAccessor { RequestContext = requestContext };
            httpContext.User = principal;
            bool nextCalled = false;
            RequestDelegate next = _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };
            using var services = new ServiceCollection()
                .AddSingleton(_logger)
                .AddSingleton(ResourceIdPolicy.From(useLongResourceIds))
                .BuildServiceProvider();
            var middleware = ActivatorUtilities.CreateInstance<SmartClinicalScopesMiddleware>(services, next);
            var authorizationService = new RoleBasedFhirAuthorizationService(authorizationConfiguration, accessor);

            await middleware.Invoke(httpContext, accessor, Options.Create(securityConfiguration), authorizationService);

            return (requestContext.AccessControlContext, nextCalled);
        }

        private static async Task<AuthorizationConfiguration> LoadRoles(AuthorizationConfiguration authConfig)
        {
            var roles = new
            {
                roles = new[]
                {
                    new
                    {
                        name = "smartUser",
                        dataActions = new[] { "*", "smart" },
                        notDataActions = new string[] { },
                        scopes = new[] { "/" },
                    },
                    new
                    {
                        name = "globalAdmin",
                        dataActions = new[] { "*" },
                        notDataActions = new string[] { },
                        scopes = new[] { "/" },
                    },
                },
            };

            IFileProvider fileProvider = Substitute.For<IFileProvider>();
            var hostEnvironment = Substitute.For<IHostEnvironment>();
            hostEnvironment.ContentRootFileProvider
                .GetFileInfo("roles.json")
                .CreateReadStream()
                .Returns(new MemoryStream(Encoding.UTF8.GetBytes(JObject.FromObject(roles).ToString())));

            authConfig.ScopesClaim = new[] { "scope", "roles" };

            var roleLoader = new RoleLoader(authConfig, hostEnvironment);
            await roleLoader.StartAsync(CancellationToken.None);
            return authConfig;
        }

        private static void ValidateErrorResponse(HttpContext context, string[] errorMessages = null, bool contain = false)
        {
            Assert.NotNull(context.Response);
            Assert.Equal(HttpStatusCode.BadRequest, (HttpStatusCode)context.Response.StatusCode);

            Assert.NotNull(context.Response.Body);
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
            var body = reader.ReadToEnd();
            var options = new JsonSerializerOptions().ForFhir(typeof(OperationOutcome).Assembly);
            var resource = JsonSerializer.Deserialize<OperationOutcome>(body, options);

            Assert.NotNull(resource);
            Assert.Single(
                resource.Issue,
                x =>
                {
                    Assert.Equal(OperationOutcome.IssueSeverity.Error, x.Severity);
                    Assert.Equal(OperationOutcome.IssueType.Invalid, x.Code);
                    if (errorMessages == null || errorMessages.Length == 0)
                    {
                        Assert.NotNull(x.Diagnostics);
                    }
                    else
                    {
                        if (contain)
                        {
                            foreach (var errorMessage in errorMessages)
                            {
                                Assert.Contains(errorMessage, x.Diagnostics);
                            }
                        }
                        else
                        {
                            Assert.Equal(errorMessages[0], x.Diagnostics);
                        }
                    }

                    Assert.NotEmpty(x.Diagnostics);
                    return true;
                });
        }
    }
}
