// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model.Primitives;

namespace Microsoft.Health.Fhir.Core.Models
{
    public class CodableConceptInfo
    {
        public CodableConceptInfo()
        {
            Coding = new List<Coding>();
        }

        public ICollection<Coding> Coding { get; }
    }
}
