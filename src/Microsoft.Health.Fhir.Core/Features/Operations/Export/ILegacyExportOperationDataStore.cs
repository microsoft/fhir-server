// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export;

/// <summary>
/// Provides access to the legacy export job data.
/// </summary>
public interface ILegacyExportOperationDataStore
{
    /// <summary>
    /// Gets an export job by id.
    /// </summary>
    /// <param name="id">The id of the job.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An instance of the existing export job.</returns>
    /// <exception cref="JobNotFoundException"> thrown when the specific <paramref name="id"/> is not found. </exception>
    Task<ExportJobOutcome> GetLegacyExportJobByIdAsync(string id, CancellationToken cancellationToken);
}
