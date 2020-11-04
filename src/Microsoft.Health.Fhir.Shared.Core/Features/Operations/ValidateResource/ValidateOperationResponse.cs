// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Operation.Validate
{
    public class ValidateOperationResponse
    {
        public ValidateOperationResponse(params OperationOutcomeIssue[] issues)
        {
            EnsureArg.IsNotNull(issues, nameof(issues));

            Issues = issues;
        }

        public ICollection<OperationOutcomeIssue> Issues { get; }
    }
}
