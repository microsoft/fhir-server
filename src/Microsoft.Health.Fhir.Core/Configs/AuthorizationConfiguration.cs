// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class AuthorizationConfiguration
    {
        public string RolesClaim { get; set; } = "roles";

        public bool Enabled { get; set; }

        public IList<Role> Roles { get; } = new List<Role>();

        public void ValidateRoles()
        {
            var issues = new List<OperationOutcomeIssue>();

            foreach (Role role in Roles)
            {
                foreach (var validationError in role.Validate(new ValidationContext(role)))
                {
                    issues.Add(new OperationOutcomeIssue(
                        OperationOutcomeConstants.IssueSeverity.Fatal,
                        OperationOutcomeConstants.IssueType.Invalid,
                        validationError.ErrorMessage));
                }
            }

            if (issues.Count > 0)
            {
                throw new InvalidDefinitionException(
                    Resources.AuthorizationPermissionDefinitionInvalid,
                    issues.ToArray());
            }
        }
    }
}
