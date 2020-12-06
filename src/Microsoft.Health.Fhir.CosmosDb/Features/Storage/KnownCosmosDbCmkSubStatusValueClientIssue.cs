// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>
    /// Cosmos DB customer-managed key (CMK) sub status values to denote client-side (end-user) issues
    /// </summary>
    public enum KnownCosmosDbCmkSubStatusValueClientIssue
    {
        // Customer-Managed Key (CMK) values
        KeyVaultAuthenticationFailure = 4002,
        KeyVaultKeyNotFound = 4003,
        KeyVaultWrapUnwrapFailure = 4005,
        InvalidKeyVaultKeyUri = 4006,
        KeyVaultDnsNotResolved = 4009,
    }
}
