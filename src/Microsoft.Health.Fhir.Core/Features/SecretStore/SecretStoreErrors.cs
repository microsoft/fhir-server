// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.SecretStore
{
    public static class SecretStoreErrors
    {
        public static string GetSecretError { get; } = Resources.UnableToGetSecret;

        public static string DeleteSecretError { get; } = Resources.UnableToDeleteSecret;

        public static string SetSecretError { get; } = Resources.UnableToSetSecret;
    }
}
