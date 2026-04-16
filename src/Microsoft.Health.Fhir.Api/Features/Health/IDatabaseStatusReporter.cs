// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Api.Features.Health
{
    /// <summary>
    /// Provides functionality to report the status of Data Store.
    /// </summary>
    public interface IDatabaseStatusReporter
    {
        /// <summary>
        /// Gets the current status of the customer-managed key.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that returns true if the key is healthy; otherwise, false.</returns>
        Task<bool> IsCustomerManagerKeyProperlySetAsync(CancellationToken cancellationToken);
    }
}
