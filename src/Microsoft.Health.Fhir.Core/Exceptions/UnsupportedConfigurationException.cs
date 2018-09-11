// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using static Hl7.Fhir.Model.OperationOutcome;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    public class UnsupportedConfigurationException : FhirException
    {
        public UnsupportedConfigurationException(string message, IssueComponent[] issues = null)
            : base(message, issues)
        {
        }
    }
}
