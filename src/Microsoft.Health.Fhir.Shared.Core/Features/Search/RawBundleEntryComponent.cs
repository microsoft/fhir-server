// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using EnsureThat;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Search
{
    [FhirType(IsResource = true)]
    public class RawBundleEntryComponent : Bundle.EntryComponent
    {
        public RawBundleEntryComponent(ResourceWrapper wrapper)
        {
            EnsureArg.IsNotNull(wrapper, nameof(wrapper));

            Wrapper = wrapper;
            Content = ResourceDeserializer.DeserializeToJsonDocument(Wrapper);
        }

        public JsonDocument Content { get; set; }

        public ResourceWrapper Wrapper { get; set; }

        public override IDeepCopyable DeepCopy()
        {
            // TODO YAZAN
            return this;
        }
    }
}
