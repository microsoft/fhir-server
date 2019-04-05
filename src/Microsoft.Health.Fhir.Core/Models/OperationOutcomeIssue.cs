// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Models
{
    public class OperationOutcomeIssue
    {
        public OperationOutcomeIssue(string severity, string code, string diagnostics)
        {
            Severity = severity;
            Code = code;
            Diagnostics = diagnostics;
        }

        public string Severity { get; }

        public string Code { get; }

        public string Diagnostics { get; }
    }
}
