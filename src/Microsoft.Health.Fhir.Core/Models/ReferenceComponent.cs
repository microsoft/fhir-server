// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Models
{
    public class ReferenceComponent
    {
        public ReferenceComponent(string reference)
        {
            EnsureArg.IsNotNullOrEmpty(reference, nameof(reference));

            Reference = reference;
        }

        public string Reference { get; }
    }
}
