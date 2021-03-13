// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Models
{
    public class ReferenceComponent
    {
        /// <summary>
        /// Gets or sets the reference of the component.
        /// </summary>
        public string Reference { get; set; }

        /// <summary>
        /// Implements the ToString method.
        /// </summary>
        /// <returns>The reference string.</returns>
        public override string ToString()
        {
            return Reference;
        }
    }
}
