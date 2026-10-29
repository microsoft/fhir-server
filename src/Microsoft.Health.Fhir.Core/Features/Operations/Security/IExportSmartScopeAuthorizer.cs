// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Messages.Export;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Security
{
    /// <summary>
    /// Authorizes SMART scope and compartment restrictions for export operations.
    /// </summary>
    public interface IExportSmartScopeAuthorizer
    {
        /// <summary>
        /// Authorizes an export request to create a job and determines the resource types to persist on it.
        /// </summary>
        /// <param name="request">The export request.</param>
        /// <returns>
        /// The canonical comma-separated resource types to persist on the export job, or the request's resource
        /// types when SMART scope authorization does not apply.
        /// </returns>
        string AuthorizeCreateAndResolveResourceType(CreateExportRequest request);

        /// <summary>
        /// Gets the resource types that the current SMART caller can export through search inclusions.
        /// </summary>
        /// <returns>
        /// Authorized resource types, including "*" for unrestricted system access, or null for a non-SMART request.
        /// </returns>
        IReadOnlyCollection<string> GetAuthorizedIncludeResourceTypes();

        /// <summary>
        /// Authorizes access to an existing export job when SMART scope authorization applies.
        /// </summary>
        /// <param name="exportJobRecord">The export job.</param>
        void AuthorizeJobAccess(ExportJobRecord exportJobRecord);
    }
}
