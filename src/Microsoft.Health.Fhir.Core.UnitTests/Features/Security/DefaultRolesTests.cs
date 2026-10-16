// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Security
{
    /// <summary>
    /// Validates the default roles.json shipped in Microsoft.Health.Fhir.Shared.Web.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Security)]
    public class DefaultRolesTests
    {
        private const string SmartUserRoleName = "smartUser";
        private const string DefaultRolesResourceName = "DefaultRoles.json";

        private const DataActions WriteDataActions =
            DataActions.Write | DataActions.Create | DataActions.Update | DataActions.Delete | DataActions.HardDelete;

        public static IEnumerable<object[]> WriteOperations()
        {
            yield return new object[] { "create" };
            yield return new object[] { "update" };
            yield return new object[] { "upsert" };
            yield return new object[] { "patch" };
            yield return new object[] { "delete" };
            yield return new object[] { "hardDelete" };
            yield return new object[] { "conditionalCreate" };
            yield return new object[] { "conditionalUpdate" };
            yield return new object[] { "conditionalPatch" };
            yield return new object[] { "conditionalDelete" };
        }

        public static IEnumerable<object[]> ReadOperations()
        {
            yield return new object[] { "read" };
            yield return new object[] { "search" };
            yield return new object[] { "export" };
        }

        [Fact]
        public async Task GivenDefaultRoles_WhenLoaded_ThenSmartUserRoleDoesNotGrantWriteDataActions()
        {
            // Arrange
            AuthorizationConfiguration authorizationConfiguration = await LoadDefaultRolesAsync();

            // Act
            Role smartUser = authorizationConfiguration.Roles.Single(r => r.Name == SmartUserRoleName);

            // Assert
            Assert.Equal(DataActions.None, smartUser.AllowedDataActions & WriteDataActions);
            Assert.True(smartUser.AllowedDataActions.HasFlag(DataActions.Smart));
        }

        [Theory]
        [MemberData(nameof(WriteOperations))]
        public async Task GivenSmartUserRoleWithAllSmartScopes_WhenCheckingWriteAccess_ThenAccessIsDenied(string operation)
        {
            // Arrange
            IAuthorizationService<DataActions> authorizationService = await CreateSmartUserAuthorizationServiceAsync();

            // Act
            bool granted = await CheckAccessAsync(authorizationService, operation);

            // Assert
            Assert.False(granted, $"The default smartUser role must not grant '{operation}'.");
        }

        [Theory]
        [MemberData(nameof(ReadOperations))]
        public async Task GivenSmartUserRoleWithAllSmartScopes_WhenCheckingReadAccess_ThenAccessIsGranted(string operation)
        {
            // Arrange
            IAuthorizationService<DataActions> authorizationService = await CreateSmartUserAuthorizationServiceAsync();

            // Act
            bool granted = await CheckAccessAsync(authorizationService, operation);

            // Assert
            Assert.True(granted, $"The default smartUser role should grant '{operation}'.");
        }

        private static Task<bool> CheckAccessAsync(IAuthorizationService<DataActions> service, string operation)
        {
            CancellationToken ct = CancellationToken.None;
            return operation switch
            {
                "create" => service.CheckCreateAccess(ct, throwException: false),
                "update" => service.CheckUpdateAccess(ct, throwException: false),
                "upsert" => service.CheckUpsertAccess(ct, throwException: false),
                "patch" => service.CheckPatchAccess(ct, throwException: false),
                "delete" => service.CheckDeleteAccess(ct, throwException: false),
                "hardDelete" => service.CheckDeleteAccess(ct, hardDelete: true, throwException: false),
                "conditionalCreate" => service.CheckConditionalCreateAccess(ct, throwException: false),
                "conditionalUpdate" => service.CheckConditionalUpdateAccess(ct, throwException: false),
                "conditionalPatch" => service.CheckConditionalPatchAccess(ct, throwException: false),
                "conditionalDelete" => service.CheckConditionalDeleteAccess(ct, throwException: false),
                "read" => service.CheckGetAccess(ct, throwException: false),
                "search" => service.CheckSearchAccess(ct, throwException: false),
                "export" => service.CheckAccess(DataActions.Export, false, ct),
                _ => throw new System.ArgumentOutOfRangeException(nameof(operation), operation, null),
            };
        }

        private static async Task<IAuthorizationService<DataActions>> CreateSmartUserAuthorizationServiceAsync()
        {
            AuthorizationConfiguration authorizationConfiguration = await LoadDefaultRolesAsync();

            var requestContext = new FhirRequestContext(
                method: "POST",
                uriString: "https://localhost/Patient",
                baseUriString: "https://localhost/",
                correlationId: "correlationId",
                requestHeaders: new Dictionary<string, StringValues>(),
                responseHeaders: new Dictionary<string, StringValues>())
            {
                Principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(authorizationConfiguration.RolesClaim, SmartUserRoleName) })),
                ResourceType = KnownResourceTypes.Patient,
            };

            // Simulate a SMART token whose scopes grant every data action on every resource type (e.g. system/*.*),
            // so any denial comes from the role rather than from the scopes.
            requestContext.AccessControlContext.ApplyFineGrainedAccessControl = true;
            requestContext.AccessControlContext.AllowedResourceActions.Add(
                new ScopeRestriction(KnownResourceTypes.All, DataActions.All, "system"));

            var requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            requestContextAccessor.RequestContext.Returns(requestContext);

            return new RoleBasedFhirAuthorizationService(authorizationConfiguration, requestContextAccessor);
        }

        private static async Task<AuthorizationConfiguration> LoadDefaultRolesAsync()
        {
            Stream defaultRoles = typeof(DefaultRolesTests).Assembly.GetManifestResourceStream(DefaultRolesResourceName);
            Assert.NotNull(defaultRoles);

            var hostEnvironment = Substitute.For<IHostEnvironment>();
            hostEnvironment.ContentRootFileProvider
                .GetFileInfo("roles.json")
                .CreateReadStream()
                .Returns(defaultRoles);

            var authorizationConfiguration = new AuthorizationConfiguration();
            var roleLoader = new RoleLoader(authorizationConfiguration, hostEnvironment);
            await roleLoader.StartAsync(CancellationToken.None);
            return authorizationConfiguration;
        }
    }
}
