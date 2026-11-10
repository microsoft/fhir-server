// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.FhirPackages
{
    /// <summary>
    /// A package's SearchParameters were not written because the data store already contains data.
    /// </summary>
    /// <remarks>
    /// Package SearchParameters are written as <c>Enabled</c> without a reindex, which is only correct while no resources
    /// other than SearchParameters exist. This failure is not retryable: the service must use the standard
    /// create-then-reindex path instead. Nothing from the package has been written.
    /// </remarks>
    public sealed class FhirPackageStoreNotEmptyException : FhirException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FhirPackageStoreNotEmptyException"/> class.
        /// </summary>
        /// <param name="packageId">The package id.</param>
        /// <param name="version">The package version.</param>
        public FhirPackageStoreNotEmptyException(string packageId, string version)
            : base(
                  Format(packageId, version),
                  new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Error, OperationOutcomeConstants.IssueType.Conflict, Format(packageId, version)))
        {
        }

        private static string Format(string packageId, string version) =>
            string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageStoreNotEmpty, packageId, version);
    }
}
