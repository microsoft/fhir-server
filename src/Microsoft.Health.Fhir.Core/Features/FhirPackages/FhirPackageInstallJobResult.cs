// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Health.Fhir.Core.Features.FhirPackages
{
    /// <summary>
    /// The latest state positively verified by an installation attempt.
    /// </summary>
    public enum FhirPackageInstallJobState
    {
        /// <summary>
        /// The job has not yet verified the complete persisted set.
        /// </summary>
        Verifying,

        /// <summary>
        /// The complete set is persisted and searchable on the current instance.
        /// </summary>
        Committed,

        /// <summary>
        /// The active hosts have converged to at least the installed status version.
        /// </summary>
        Converged,
    }

    /// <summary>
    /// Durable outcome of a completed or failed package installation.
    /// </summary>
    public sealed class FhirPackageInstallJobResult
    {
        /// <summary>
        /// Gets or sets the package name.
        /// </summary>
        public string PackageId { get; set; }

        /// <summary>
        /// Gets or sets the exact package version.
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Gets or sets the verified package integrity.
        /// </summary>
        public string Integrity { get; set; }

        /// <summary>
        /// Gets or sets the latest verified installation state.
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public FhirPackageInstallJobState State { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether a failed attempt can be retried.
        /// </summary>
        public bool IsRetryable { get; set; }

        /// <summary>
        /// Gets or sets the package's SearchParameter canonical URLs.
        /// </summary>
        public IReadOnlyList<string> SearchParameterUrls { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets informational and error issues.
        /// </summary>
        public IReadOnlyList<OperationOutcomeIssue> Issues { get; set; } = Array.Empty<OperationOutcomeIssue>();

        /// <summary>
        /// Gets or sets the failure diagnostic, when present.
        /// </summary>
        public string Message { get; set; }
    }
}
