// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.FhirPackages
{
    /// <summary>
    /// A FHIR package was rejected. Nothing from the package has been written.
    /// </summary>
    public sealed class FhirPackageException : FhirException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FhirPackageException"/> class with one error issue.
        /// </summary>
        /// <param name="issueType">The OperationOutcome issue type.</param>
        /// <param name="diagnostics">The actionable diagnostic message.</param>
        public FhirPackageException(string issueType, string diagnostics)
            : base(diagnostics, new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Error, issueType, diagnostics))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirPackageException"/> class with the issues found.
        /// </summary>
        /// <param name="issues">Every issue found; at least one should be an error.</param>
        public FhirPackageException(IEnumerable<OperationOutcomeIssue> issues)
            : base(Core.Resources.FhirPackageRejected, issues?.ToArray())
        {
        }
    }
}
