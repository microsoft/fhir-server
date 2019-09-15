// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    public class RequestNotValidException : FhirException
    {
        public RequestNotValidException(string message)
            : base(message)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            Issues.Add(new OperationOutcomeIssue(
                IssueSeverity.Error,
                IssueType.Invalid,
                message));
        }
    }
}
