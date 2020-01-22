// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.AccessTokenProvider
{
    public class UnsupportedAccessTokenProviderException : FhirException
    {
        public UnsupportedAccessTokenProviderException(string accessTokenProviderType)
            : base(string.Format(Resources.UnsupportedAccessTokenProvider, accessTokenProviderType))
        {
            Issues.Add(new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Error,
                OperationOutcomeConstants.IssueType.NotFound,
                Message));
        }
    }
}
