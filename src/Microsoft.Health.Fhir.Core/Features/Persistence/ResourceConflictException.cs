// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class ResourceConflictException : FhirException
    {
        public ResourceConflictException(WeakETag etag)
        {
            Debug.Assert(etag != null, "ETag should not be null");

            WeakETag = etag;
            Issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Conflict,
                    string.Format(Core.Resources.ResourceVersionConflict, etag?.VersionId)));
        }

        public ResourceConflictException(string message)
        {
            EnsureArg.IsNotNullOrWhiteSpace(message, nameof(message));

            Issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Conflict,
                    message));
        }

        /// <summary>
        /// Gets the ETag that caused this version conflict, if one was supplied.
        /// </summary>
        public WeakETag WeakETag { get; }
    }
}
