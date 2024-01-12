// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public sealed class CustomerManagedKeyEncryption
    {
        public EncryptionKeyIdentity KeyEncryptionKeyIdentity { get; set; } = new EncryptionKeyIdentity();

        public Uri KeyEncryptionKeyUrl { get; set; }
    }
}
