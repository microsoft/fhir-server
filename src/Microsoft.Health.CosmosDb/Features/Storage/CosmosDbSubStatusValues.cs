// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    public static class CosmosDbSubStatusValues
    {
        // Customer Managed Key (CMK) values
        public const int CmkAadClientCredentialsGrantFailure = 4000;
        public const int CmkAadServiceUnavailable = 4001;
        public const int CmkKeyVaultAuthenticationFailure = 4002;
        public const int CmkKeyVaultKeyNotFound = 4003;
        public const int CmkKeyVaultServiceUnavailable = 4004;
        public const int CmkKeyVaultWrapUnwrapFailure = 4005;
        public const int CmkInvalidKeyVaultKeyUri = 4006;
        public const int CmkInvalidInputBytes = 4007;
        public const int CmkKeyVaultInternalServerError = 4008;
        public const int CmkKeyVaultDnsNotResolved = 4009;
    }
}
