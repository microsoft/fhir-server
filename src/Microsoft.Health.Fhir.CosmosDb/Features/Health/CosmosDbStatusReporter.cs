// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Fhir.Core.Data;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Health
{
    /// <summary>
    /// Cosmos DB implementation of <see cref="BaseDatabaseStatusReporter"/>.
    /// </summary>
    public class CosmosDbStatusReporter : BaseDatabaseStatusReporter
    {
        public CosmosDbStatusReporter()
            : base()
        {
        }

        public override bool IsCustomerManagedKeyException(Exception exception)
        {
            return exception is CosmosException cdbe && cdbe.IsCustomerManagedKeyException();
        }

        public override Task<bool> IsCustomerManagedKeyProperlySetAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(GetDatabaseAvailability() != DatabaseAvailability.DegradedByCustomerManagedKey);
        }
    }
}
