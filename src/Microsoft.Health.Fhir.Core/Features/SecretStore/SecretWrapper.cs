// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.SecretStore
{
    /// <summary>
    /// Class that acts as a wrapper around the data that needs to be stored in <see cref="ISecretStore"/>.
    /// Currently only contains the secret name and value, but can be expanded depending on usage.
    /// </summary>
    public class SecretWrapper
    {
        public SecretWrapper(string secretName, string secretValue)
        {
            EnsureArg.IsNotNullOrWhiteSpace(secretName, nameof(secretName));
            EnsureArg.IsNotEmptyOrWhitespace(secretValue, nameof(secretValue));

            SecretName = secretName;
            SecretValue = secretValue;
        }

        public string SecretName { get; }

        public string SecretValue { get; }
    }
}
