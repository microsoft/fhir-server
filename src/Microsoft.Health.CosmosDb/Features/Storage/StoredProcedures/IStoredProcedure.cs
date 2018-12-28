// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Azure.Documents;

namespace Microsoft.Health.CosmosDb.Features.Storage.StoredProcedures
{
    public interface IStoredProcedure
    {
        string FullName { get; }

        Uri GetUri(Uri collection);

        StoredProcedure AsStoredProcedure();
    }
}
