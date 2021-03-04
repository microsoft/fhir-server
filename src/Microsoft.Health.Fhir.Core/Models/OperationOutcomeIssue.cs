// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Models
{
    public class OperationOutcomeIssue
    {
        public OperationOutcomeIssue(
             string severity,
             string code,
             string diagnostics = null,
             CodableConceptInfo detailsCodes = null,
             string detailsText = null,
             string[] location = null)
        {
            EnsureArg.IsNotNullOrEmpty(severity, nameof(severity));
            EnsureArg.IsNotNullOrEmpty(code, nameof(code));

            Severity = severity;
            Code = code;
            DetailsCodes = detailsCodes;
            DetailsText = detailsText;
            Diagnostics = diagnostics;
            Location = location;
        }

        public string Severity { get; }

        public string Code { get; }

        public CodableConceptInfo DetailsCodes { get; }

        public string DetailsText { get; }

        public string Diagnostics { get; }

        public ICollection<string> Location { get; }
    }
}
