// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.FhirPackages
{
    /// <summary>
    /// Reports a package acquisition failure and whether another attempt can succeed.
    /// </summary>
    public sealed class FhirPackageAcquisitionException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FhirPackageAcquisitionException"/> class.
        /// </summary>
        /// <param name="message">A diagnostic that does not contain credentials or source URLs.</param>
        /// <param name="isRetryable">Whether another attempt might succeed without changing the package request.</param>
        /// <param name="innerException">The underlying safe diagnostic, if any.</param>
        public FhirPackageAcquisitionException(string message, bool isRetryable, Exception innerException = null)
            : base(message, innerException)
        {
            IsRetryable = isRetryable;
        }

        /// <summary>
        /// Gets a value indicating whether the failed acquisition can be retried.
        /// </summary>
        public bool IsRetryable { get; }
    }
}
