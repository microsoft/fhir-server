// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Azure.Cosmos.Scripts;

namespace Microsoft.Health.CosmosDb.Features.Storage.StoredProcedures
{
    public interface IStoredProcedure
    {
        string FullName { get; }

        StoredProcedureProperties AsStoredProcedure();
    }
}
