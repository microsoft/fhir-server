// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class AuthorizationConfiguration
    {
        public const string RolesClaim = "roles";

        public bool Enabled { get; set; }

        public IList<Role> Roles { get; set; } = new List<Role>();

        public void ValidateRoles()
        {
            var issues = new List<OperationOutcome.IssueComponent>();

            foreach (Role role in Roles)
            {
                foreach (var validationError in role.Validate(new ValidationContext(role)))
                {
                    issues.Add(new OperationOutcome.IssueComponent
                    {
                        Severity = OperationOutcome.IssueSeverity.Fatal,
                        Code = OperationOutcome.IssueType.Invalid,
                        Diagnostics = validationError.ErrorMessage,
                    });
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
