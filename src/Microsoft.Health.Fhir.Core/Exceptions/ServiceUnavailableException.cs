// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    public class ServiceUnavailableException : FhirException
    {
        public ServiceUnavailableException()
            : this(Resources.ServiceUnavailable)
        {
        }

        public ServiceUnavailableException(string message)
            : base(message)
        {
            Issues.Add(new OperationOutcome.IssueComponent
            {
                Severity = OperationOutcome.IssueSeverity.Error,
                Code = OperationOutcome.IssueType.Processing,
                Diagnostics = message,
            });
        }
    }
}
