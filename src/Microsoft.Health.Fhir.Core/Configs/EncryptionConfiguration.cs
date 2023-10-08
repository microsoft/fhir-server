// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    public sealed class EncryptionConfiguration
    {
        public CustomerManagedKeyEncryption CustomerManagedKeyEncryption { get; set; }

        public bool IsEncryptionSet()
        {
            if (CustomerManagedKeyEncryption != null)
            {
                if (CustomerManagedKeyEncryption.KeyEncryptionKeyUrl != null)
                {
                    return true;
                }

                if (CustomerManagedKeyEncryption.KeyEncryptionKeyIdentity != null)
                {
                    if (!string.IsNullOrEmpty(CustomerManagedKeyEncryption.KeyEncryptionKeyIdentity.FederatedClientId))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
