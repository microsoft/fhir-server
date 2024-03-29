// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    public sealed class InstanceUnavailableException : FhirException
    {
        public InstanceUnavailableException(string message, Exception innerException = null)
            : base(message, innerException)
        {
            EnsureArg.IsNotNullOrEmpty(message, nameof(message));

            Issues.Add(new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                OperationOutcomeConstants.IssueType.Transient,
                message));
        }
    }
}
