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
        private InvalidDefinitionException _deserializationException;

        public AuthPermissionConfigTests()
        {
            var invalidJson1 = Samples.GetJson("InValidAuth1");
            var invalidJson2 = Samples.GetJson("InValidAuth2");

            try
            {
                RoleConfiguration.ValidateAndGetRoleConfiguration(invalidJson1);
            }
            catch (InvalidDefinitionException ex)
            {
                _validationException = ex;
            }

            try
            {
                RoleConfiguration.ValidateAndGetRoleConfiguration(invalidJson2);
            }
            catch (InvalidDefinitionException ex)
            {
                _deserializationException = ex;
            }
        }

        [Fact]
        public void NoError_On_Valid_AuthPermission()
        {
            var validJson = Samples.GetJson("ValidAuth");
            var roleConfig = RoleConfiguration.ValidateAndGetRoleConfiguration(validJson);
            Assert.NotNull(roleConfig);
            Assert.NotNull(roleConfig.Roles);
            Assert.Equal(3, roleConfig.Roles.Count());
            return;
        }

        [Fact]
        public void Invalid_TemplateExpresssion_On_ClinicianRole()
        {
            Assert.NotNull(_validationException.Issues.SingleOrDefault(issueComp => issueComp.Diagnostics.Equals("Rolepermission for Role clinician has invalid filter expression.")));
        }

        [Fact]
        public void Invalid_Actions_OnNurseRole()
        {
            Assert.NotNull(_validationException.Issues.SingleOrDefault(issueComp => issueComp.Diagnostics.Equals("Rolepermission for Role Nurse does not have any Actions.")));
        }

        [Fact]
        public void Invalid2_Actions_OnAdminRole()
        {
            Assert.NotNull(_deserializationException.Issues.SingleOrDefault(issueComp => issueComp.Diagnostics.Contains("Error converting value \"SomeAction\" to type 'Microsoft.Health.Fhir.Core.Features.Security.ResourceAction'.")));
        }
    }
}
