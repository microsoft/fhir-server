// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    /// <summary>
    /// The exception that is thrown when provided definition is invalid.
    /// </summary>
    public class InvalidDefinitionException : FhirException
    {
        public InvalidDefinitionException(string message, OperationOutcomeIssue[] issues = null)
            : base(message, issues)
        {
        }
    }
}
