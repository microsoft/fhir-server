// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.FhirPackages
{
    /// <summary>
    /// A package's SearchParameters were not written because the write conflicted with other state. Nothing was written.
    /// </summary>
    /// <remarks>
    /// When <see cref="IsRetryable"/> is <c>true</c> the conflict was transient, such as a concurrent SearchParameter
    /// change or a running reindex job, and the installation can be retried. Otherwise the conflict is with persisted
    /// state, such as a different version of the package already being installed, and retrying won't help.
    /// </remarks>
    public sealed class FhirPackageWriteConflictException : FhirException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FhirPackageWriteConflictException"/> class.
        /// </summary>
        /// <param name="message">The actionable diagnostic message.</param>
        /// <param name="isRetryable">Whether retrying the installation can succeed.</param>
        /// <param name="innerException">The underlying conflict, if any.</param>
        public FhirPackageWriteConflictException(string message, bool isRetryable, Exception innerException = null)
            : base(
                  message,
                  innerException,
                  new OperationOutcomeIssue(
                      OperationOutcomeConstants.IssueSeverity.Error,
                      isRetryable ? OperationOutcomeConstants.IssueType.Transient : OperationOutcomeConstants.IssueType.Conflict,
                      message))
        {
            IsRetryable = isRetryable;
        }

        /// <summary>
        /// Gets a value indicating whether retrying the installation can succeed.
        /// </summary>
        public bool IsRetryable { get; }
    }
}
