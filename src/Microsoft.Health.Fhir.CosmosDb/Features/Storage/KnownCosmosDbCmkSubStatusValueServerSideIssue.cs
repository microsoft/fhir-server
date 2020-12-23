// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>
    /// Cosmos DB customer-managed key (CMK) sub status values to denote server-side issues
    /// </summary>
    public enum KnownCosmosDbCmkSubStatusValueServerSideIssue
    {
        // Customer-Managed Key (CMK) values
        AadClientCredentialsGrantFailure = 4000,
        AadServiceUnavailable = 4001,
        KeyVaultServiceUnavailable = 4004,
        KeyVaultInternalServerError = 4008,
    }
}
