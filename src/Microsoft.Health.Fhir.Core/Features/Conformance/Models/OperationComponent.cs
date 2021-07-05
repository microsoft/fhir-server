// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    public class OperationComponent
    {
        public string Name { get; set; }

        /// <summary>
        /// Defines the Reference, this will be formatted into the correct FHIR version with <see cref="ReferenceComponentConverter"/>
        /// </summary>
        public ReferenceComponent Definition { get; set; }
    }
}
