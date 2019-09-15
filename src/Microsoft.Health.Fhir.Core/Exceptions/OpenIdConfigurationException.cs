// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    public class OpenIdConfigurationException : FhirException
    {
        public OpenIdConfigurationException()
        {
            Issues.Add(new OperationOutcomeIssue(
                IssueSeverity.Error,
                IssueType.Security,
                Resources.OpenIdConfiguration));
        }
    }
}
