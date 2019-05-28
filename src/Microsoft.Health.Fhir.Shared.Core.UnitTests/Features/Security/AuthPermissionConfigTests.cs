// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Tests.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Security
{
    public class AuthPermissionConfigTests
    {
        [Fact]
        public void GivenAValidAuthorizationConfiguration_WhenDeserailized_ThenReturnsExpectedRoleInformation()
        {
            var authorizationConfiguration = Samples.GetJsonSample<AuthorizationConfiguration>("AuthConfigWithValidRoles");
            authorizationConfiguration.ValidateRoles();

            Assert.NotNull(authorizationConfiguration);
            Assert.NotNull(authorizationConfiguration.Roles);
            Assert.Equal(3, authorizationConfiguration.Roles.Count());
        }

        [Fact]
        public void GivenAnInvalidAuthorizationConfigurationForRoleWithNoPermissions_WhenValidated_ThrowAppropriateValidationException()
        {
            var invalidAuthorizationConfiguration = Samples.GetJsonSample<AuthorizationConfiguration>("AuthConfigWithValidRoles");
            invalidAuthorizationConfiguration.Roles[0].ResourcePermissions.Clear();

            InvalidDefinitionException validationException = Assert.Throws<InvalidDefinitionException>(() => invalidAuthorizationConfiguration.ValidateRoles());

            Assert.NotNull(validationException.Issues.SingleOrDefault(issueComp => issueComp.Diagnostics.Equals("Role must have one or more resource permissions.")));
        }

        [Fact]
        public void GivenAnInvalidAuthorizationConfigurationForRoleWithNoActions_WhenValidated_ThrowAppropriateValidationException()
        {
            var invalidAuthorizationConfiguration = Samples.GetJsonSample<AuthorizationConfiguration>("AuthConfigWithInvalidEntries");
            InvalidDefinitionException validationException = Assert.Throws<InvalidDefinitionException>(() => invalidAuthorizationConfiguration.ValidateRoles());

            Assert.NotNull(validationException.Issues.SingleOrDefault(issueComp => issueComp.Diagnostics.Equals("Role contains a resource permissions with no actions.")));
        }
    }
}
