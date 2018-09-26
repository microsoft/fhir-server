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
        private InvalidDefinitionException _validationException;

        public AuthPermissionConfigTests()
        {
            var invalidJson1 = Samples.GetJsonSample<RoleConfiguration>("AuthConfigWIthInvalidEntries");

            try
            {
                invalidJson1.Validate();
            }
            catch (InvalidDefinitionException ex)
            {
                _validationException = ex;
            }
        }

        [Fact]
        public void NoError_On_Valid_AuthPermission()
        {
            var roleConfig = Samples.GetJsonSample<RoleConfiguration>("AuthConfigWithValidRoles");
            roleConfig.Validate();
            Assert.NotNull(roleConfig);
            Assert.NotNull(roleConfig.Roles);
            Assert.Equal(3, roleConfig.Roles.Count());
            return;
        }

        [Fact]
        public void Invalid_Actions_OnNurseRole()
        {
            Assert.NotNull(_validationException.Issues.SingleOrDefault(issueComp => issueComp.Diagnostics.Equals("ResourcePermission for Role Nurse does not have any Actions.")));
        }
    }
}
