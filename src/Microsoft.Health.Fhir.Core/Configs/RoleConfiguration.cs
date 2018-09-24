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
        public virtual IEnumerable<Role> Roles { get; set; }

        public static RoleConfiguration ValidateAndGetRoleConfiguration(string authjson)
        {
            var issues = new List<IssueComponent>();

            RoleConfiguration roleConfiguration = JsonConvert.DeserializeObject<RoleConfiguration>(
            authjson,
            new JsonSerializerSettings
            {
                Error = (sender, args) =>
                {
                    AddIssue(args.ErrorContext.Error.Message);
                    args.ErrorContext.Handled = true;
                },
            });

            EnsureNoIssues();

            foreach (Role role in roleConfiguration.Roles)
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

            EnsureNoIssues();

            return roleConfiguration;

            void AddIssue(string format, params object[] args)
            {
                issues.Add(new IssueComponent()
                {
                    Severity = IssueSeverity.Fatal,
                    Code = IssueType.Invalid,
                    Diagnostics = string.Format(CultureInfo.InvariantCulture, format, args),
                });
            }

            void EnsureNoIssues()
            {
                if (issues.Count != 0)
                {
                    throw new InvalidDefinitionException(
                        "The authorization permission definition contains one or more invalid entries.",
                        issues.ToArray());
                }
            }
        }
    }
}
