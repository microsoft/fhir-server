// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Api.Features.Health;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Health
{
    /// <summary>
    /// Cosmos DB implementation of <see cref="IDatabaseStatusReporter"/>.
    /// Always returns healthy status without performing any checks.
    /// </summary>
    public class CosmosDbStatusReporter : IDatabaseStatusReporter
    {
        /// <inheritdoc />
        public Task<bool> IsCustomerManagerKeyProperlySetAsync(CancellationToken cancellationToken = default)
        {
            // [WI] to implement: https://microsofthealth.visualstudio.com/Health/_workitems/edit/166817
            return Task.FromResult(true);
        }
    }
}
