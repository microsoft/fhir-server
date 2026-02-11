// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    /// <summary>
    /// Represents a provider capability that is responsible for updating a section of the capability statement.
    /// </summary>
    public interface IVolatileProvideCapability : IProvideCapability
    {
        /// <summary>
        /// Updates a section of the capability statement.
        /// </summary>
        /// <param name="builder">The capability statement builder.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task UpdateAsync(ICapabilityStatementBuilder builder, CancellationToken cancellationToken);
    }
}
