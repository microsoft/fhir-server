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
        AadClientCredentialsGrantFailure = KnownCosmosDbCmkSubStatusValueServerSideIssue.AadClientCredentialsGrantFailure,
        AadServiceUnavailable = KnownCosmosDbCmkSubStatusValueServerSideIssue.AadServiceUnavailable,
        KeyVaultAuthenticationFailure = KnownCosmosDbCmkSubStatusValueClientIssue.KeyVaultAuthenticationFailure,
        KeyVaultKeyNotFound = KnownCosmosDbCmkSubStatusValueClientIssue.KeyVaultKeyNotFound,
        KeyVaultServiceUnavailable = KnownCosmosDbCmkSubStatusValueServerSideIssue.KeyVaultServiceUnavailable,
        KeyVaultWrapUnwrapFailure = KnownCosmosDbCmkSubStatusValueClientIssue.KeyVaultWrapUnwrapFailure,
        InvalidKeyVaultKeyUri = KnownCosmosDbCmkSubStatusValueClientIssue.InvalidKeyVaultKeyUri,
        KeyVaultInternalServerError = KnownCosmosDbCmkSubStatusValueServerSideIssue.KeyVaultInternalServerError,
        KeyVaultDnsNotResolved = KnownCosmosDbCmkSubStatusValueClientIssue.KeyVaultDnsNotResolved,
    }
}
