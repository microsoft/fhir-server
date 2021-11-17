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

        // It has been confirmed by the Cosmos DB team that a SubStatusCode of value '3', although not listed in
        // https://docs.microsoft.com/en-us/rest/api/cosmos-db/http-status-codes-for-cosmosdb#substatus-codes-for-end-user-issues
        // is a possible CMK SubStatusCode value in some scenarios if the customer has removed access to their key.
        // This SubStatusCode value is associated with an "The requested operation cannot be performed at this region" error message.
        RequestedOperationCannotBePerformedAtThisRegion = 3,
        KeyVaultAuthenticationFailure = 4002,
        KeyVaultKeyNotFound = 4003,
        KeyVaultWrapUnwrapFailure = 4005,
        InvalidKeyVaultKeyUri = 4006,
        KeyVaultDnsNotResolved = 4009,
    }
}
