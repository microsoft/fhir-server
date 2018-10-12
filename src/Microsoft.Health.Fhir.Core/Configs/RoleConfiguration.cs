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
        public IList<Role> Roles { get; } = new List<Role>();

        public void Validate()
        {
            List<IssueComponent> issues = null;

            foreach (Role role in Roles)
            {
                var validationErrors = role.Validate(new ValidationContext(role));

                foreach (var validationError in validationErrors)
                {
                    if (issues == null)
                    {
                        issues = new List<IssueComponent>();
                    }

                    issues.Add(new IssueComponent()
                    {
                        Severity = IssueSeverity.Fatal,
                        Code = IssueType.Invalid,
                        Diagnostics = validationError.ErrorMessage,
                    });
                }
            }

            if (issues != null && issues.Count != 0)
            {
                throw new InvalidDefinitionException(
                    Resources.AuthorizationPermissionDefinitionInvalid,
                    issues.ToArray());
            }
        }
    }
}
