// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using static Hl7.Fhir.Model.OperationOutcome;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class RoleConfiguration
    {
        public IList<Role> Roles { get; internal set; } = new List<Role>();

        public void Validate()
        {
            var issues = new List<IssueComponent>();

            foreach (Role role in Roles)
            {
                foreach (var validationError in role.Validate(new ValidationContext(role)))
                {
                    issues.Add(new IssueComponent
                    {
                        Severity = IssueSeverity.Fatal,
                        Code = IssueType.Invalid,
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
