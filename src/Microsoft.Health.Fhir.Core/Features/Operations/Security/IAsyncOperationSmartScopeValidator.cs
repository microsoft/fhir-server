// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Security
{
    /// <summary>
    /// Provides SMART on FHIR scope authorization checks for asynchronous operation
    /// status and cancellation endpoints. These checks are applied in addition to the
    /// coarse route-level authorization performed by the individual request handlers.
    /// </summary>
    public interface IAsyncOperationSmartScopeValidator
    {
        /// <summary>
        /// Validates that a SMART fine-grained restricted caller has read access to the
        /// resource types covered by the supplied export job. For non-SMART/non-fine-grained
        /// requests this is a no-op so existing admin behavior is preserved.
        /// </summary>
        /// <param name="exportJobRecord">The export job record whose status/result is being fetched.</param>
        bool ValidateExportStatusAccess(ExportJobRecord exportJobRecord);

        /// <summary>
        /// Validates that a SMART fine-grained restricted caller has both all-resource read and
        /// all-resource write access. For non-SMART/non-fine-grained requests this is a no-op so
        /// existing admin behavior is preserved.
        /// </summary>
        bool ValidateAllResourceReadWriteAccess();
    }
}
