// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Models
{
    public class ReferenceComponent
    {
        public string Reference { get; set; }

        public override string ToString()
        {
            return Reference;
        }
    }
}
