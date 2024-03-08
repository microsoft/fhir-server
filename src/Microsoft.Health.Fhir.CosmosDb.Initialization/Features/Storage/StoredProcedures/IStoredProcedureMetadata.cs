// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos.Scripts;

namespace Microsoft.Health.Fhir.CosmosDb.Initialization.Features.Storage.StoredProcedures
{
    public interface IStoredProcedureMetadata
    {
        string FullName { get; }

        StoredProcedureProperties ToStoredProcedureProperties();
    }
}
