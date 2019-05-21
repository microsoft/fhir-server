// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.Models
{
    /// <summary>
    /// Class used to wrap the export job related responses from <see cref="IFhirDataStore"/>.
    /// </summary>
    public class ExportJobOutcome
    {
        public ExportJobOutcome(ExportJobRecord jobRecord, WeakETag eTag)
        {
            EnsureArg.IsNotNull(jobRecord, nameof(jobRecord));
            EnsureArg.IsNotNull(eTag, nameof(eTag));

            JobRecord = jobRecord;
            ETag = eTag;
        }

        /// <summary>
        /// Metadata for the export job.
        /// </summary>
        public ExportJobRecord JobRecord { get; }

        /// <summary>
        /// Represents the version of the document in the datastore. Used to resolve conflicts.
        /// </summary>
        public WeakETag ETag { get; }
    }
}
