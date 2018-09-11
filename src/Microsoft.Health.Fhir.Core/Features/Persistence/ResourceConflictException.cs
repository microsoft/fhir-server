// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Exceptions;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class ResourceConflictException : FhirException
    {
        public ResourceConflictException(WeakETag etag)
        {
            Debug.Assert(etag != null, "ETag should not be null");

            Issues.Add(new OperationOutcome.IssueComponent
            {
                Severity = OperationOutcome.IssueSeverity.Error,
                Code = OperationOutcome.IssueType.Conflict,
                Diagnostics = string.Format(Core.Resources.ResourceVersionConflict, etag?.VersionId),
            });
        }
    }
}
