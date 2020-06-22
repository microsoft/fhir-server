// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>
    /// Cosmos DB customer-managed key (CMK) sub status values
    /// </summary>
    public enum KnownCosmosDbCmkSubStatusValue
    {
        // Customer-Managed Key (CMK) values
        AadClientCredentialsGrantFailure = 4000,
        AadServiceUnavailable = 4001,
        KeyVaultAuthenticationFailure = 4002,
        KeyVaultKeyNotFound = 4003,
        KeyVaultServiceUnavailable = 4004,
        KeyVaultWrapUnwrapFailure = 4005,
        InvalidKeyVaultKeyUri = 4006,
        InvalidInputBytes = 4007,
        KeyVaultInternalServerError = 4008,
        KeyVaultDnsNotResolved = 4009,
    }
}
