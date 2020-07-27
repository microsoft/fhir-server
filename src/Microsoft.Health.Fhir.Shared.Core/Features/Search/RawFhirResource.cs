// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Search
{
    [FhirType(IsResource = true)]
    public class RawFhirResource : Bundle.EntryComponent
    {
        public JsonDocument Content { get; set; }

        public override IDeepCopyable DeepCopy()
        {
            // TODO YAZAN
            return this;
        }
    }
}
