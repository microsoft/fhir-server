// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Newtonsoft.Json;
using static Hl7.Fhir.Model.OperationOutcome;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class RoleConfiguration
    {
        public RoleConfiguration()
        {
        }

        [JsonConstructor]
        public RoleConfiguration(IReadOnlyList<Role> roles)
        {
            Roles = roles;
        }

        public virtual IReadOnlyList<Role> Roles { get; set; }

        public void Validate()
        {
            var issues = new List<IssueComponent>();

            foreach (Role role in Roles)
            {
                var validationErrors = role.Validate(new ValidationContext(role));

                if (validationErrors.Any())
                {
                    foreach (var validationError in validationErrors)
                    {
                        AddIssue(validationError.ErrorMessage);
                    }
                }
            }

            if (issues.Count != 0)
            {
                throw new InvalidDefinitionException(
                    Core.Resources.AuthorizationPermissionDefinitionInvalid,
                    issues.ToArray());
            }

            return;

            void AddIssue(string format, params object[] args)
            {
                issues.Add(new IssueComponent()
                {
                    Severity = IssueSeverity.Fatal,
                    Code = IssueType.Invalid,
                    Diagnostics = string.Format(CultureInfo.InvariantCulture, format, args),
                });
            }
        }
    }
}
