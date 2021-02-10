// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    public sealed class ProfileValidationFailedException : FhirException
    {
        public ProfileValidationFailedException(OperationOutcomeIssue[] issues)
            : base(issues)
        {
        }
    }
}
