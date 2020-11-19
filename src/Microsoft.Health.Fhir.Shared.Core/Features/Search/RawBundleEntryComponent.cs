// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Search
{
    [FhirType("EntryComponent")]
    public class RawBundleEntryComponent : Bundle.EntryComponent
    {
        public RawBundleEntryComponent(ResourceWrapper resourceWrapper)
        {
            EnsureArg.IsNotNull(resourceWrapper, nameof(resourceWrapper));

            ResourceElement = new RawResourceElement(resourceWrapper);
        }

        public RawResourceElement ResourceElement { get; set; }

        public override IDeepCopyable DeepCopy()
        {
            if (Resource != null)
            {
                return base.DeepCopy();
            }

            throw new NotSupportedException();
        }
    }
}
