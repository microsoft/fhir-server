﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    public class PreconditionFailedException : FhirException
    {
        public PreconditionFailedException(string message)
            : base(message)
        {
            Debug.Assert(!string.IsNullOrEmpty(message), "Exception message should not be empty");

            Issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Required,
                    message));
        }
    }
}
