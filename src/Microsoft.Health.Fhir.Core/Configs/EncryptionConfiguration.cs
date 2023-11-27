// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    public sealed class EncryptionConfiguration
    {
        public CustomerManagedKeyEncryption CustomerManagedKeyEncryption { get; set; } = new CustomerManagedKeyEncryption();

        public bool IsEncryptionSet()
        {
            if (CustomerManagedKeyEncryption != null)
            {
                // KeyEncryptionKeyUrl is the only required property.
                // KeyEncryptionKeyIdentity.FederatedClientId is optional.
                if (CustomerManagedKeyEncryption.KeyEncryptionKeyUrl != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
