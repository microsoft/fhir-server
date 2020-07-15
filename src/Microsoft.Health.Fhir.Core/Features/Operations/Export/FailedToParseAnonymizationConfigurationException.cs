// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    public class FailedToParseAnonymizationConfigurationException : FhirException
    {
        public FailedToParseAnonymizationConfigurationException()
        : base()
            {
            }

        public FailedToParseAnonymizationConfigurationException(string message)
            : base(message)
        {
            Issues.Add(new Models.OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                OperationOutcomeConstants.IssueType.Exception,
                message));
        }

        public FailedToParseAnonymizationConfigurationException(string message, Exception exception)
            : base(message, exception)
        {
            Issues.Add(new Models.OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                OperationOutcomeConstants.IssueType.Exception,
                message));
        }
    }
}
