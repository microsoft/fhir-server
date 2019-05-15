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
        public OperationOutcomeIssue(string severity, string code, string diagnostics, string[] location = null)
        {
            EnsureArg.IsNotNullOrEmpty(severity, nameof(severity));
            EnsureArg.IsNotNullOrEmpty(code, nameof(code));
            EnsureArg.IsNotNullOrEmpty(diagnostics, nameof(diagnostics));

            Severity = severity;
            Code = code;
            Diagnostics = diagnostics;
            Location = location;
        }

        public string Severity { get; }

        public string Code { get; }

        public string Diagnostics { get; }

        public ICollection<string> Location { get; }
    }
}
