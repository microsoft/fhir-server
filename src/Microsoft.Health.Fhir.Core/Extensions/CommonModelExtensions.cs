// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class CommonModelExtensions
    {
        public static OperationOutcome.IssueComponent ToPoco(this OperationOutcomeIssue issue)
        {
            EnsureArg.IsNotNull(issue, nameof(issue));

            CodeableConcept details = null;
            var coding = new List<Coding>();
            if (issue.DetailsCodes != null)
            {
                coding = issue.DetailsCodes.Coding.Select(x => new Coding(x.System, x.Code, x.Display)).ToList();
            }

            if (coding.Count != 0 || issue.DetailsText != null)
            {
                details = new CodeableConcept()
                {
                    Coding = coding,
                    Text = issue.DetailsText,
                };
            }

            return new OperationOutcome.IssueComponent
            {
                Severity = Enum.Parse<OperationOutcome.IssueSeverity>(issue.Severity),
                Code = Enum.Parse<OperationOutcome.IssueType>(issue.Code),
                Details = details,
                Diagnostics = issue.Diagnostics,
#pragma warning disable CS0618 // Type or member is obsolete
                Location = issue.Location,
#pragma warning restore CS0618 // Type or member is obsolete
                Expression = issue.Expression,
            };
        }
    }
}
