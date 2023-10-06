// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    public sealed class EncryptionConfiguration
    {
        public CustomerManagedKeyEncryption CustomerManagedKeyEncryption { get; set; }
    }
}
