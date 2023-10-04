// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public sealed class CustomerManagedKeyEncryption
    {
        public EncryptionKeyIdentity KeyEncryptionKeyIdentity { get; set; }

        public Uri KeyEncryptionKeyUrl { get; set; }
    }
}
