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
        public void GivenAValidRoleConfiguration_WhenDeserailized_ThenReturnsExpectedRoleInformation()
        {
            var roleConfig = Samples.GetJsonSample<RoleConfiguration>("AuthConfigWithValidRoles");
            Assert.NotNull(roleConfig);
            roleConfig.Validate();
            Assert.NotNull(roleConfig.Roles);
            Assert.Equal(3, roleConfig.Roles.Count());
            return;
        }

        [Fact]
        public void GivenAnInvalidRoleConfigurationForRoleWithNoActions_WhenValidated_ThrowAppropriateValidationException()
        {
            RoleConfiguration invalidJson1 = Samples.GetJsonSample<RoleConfiguration>("AuthConfigWithInvalidEntries");
            InvalidDefinitionException validationException = Assert.Throws<InvalidDefinitionException>(() => invalidJson1.Validate());

            Assert.NotNull(validationException.Issues.SingleOrDefault(issueComp => issueComp.Diagnostics.Equals("ResourcePermission for Role 'Nurse' does not have any Actions.")));
        }
    }
}
