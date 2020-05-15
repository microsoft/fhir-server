// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;

namespace Microsoft.Health.Fhir.Core.Features.Definition.BundleNavigators
{
    internal class BundleEntryWrapper
    {
        private readonly ITypedElement _entry;

        public BundleEntryWrapper(ITypedElement entry)
        {
            EnsureArg.IsNotNull(entry, nameof(entry));

            _entry = entry;
        }

        public ITypedElement GetResource()
        {
            return _entry.Select("resource").FirstOrDefault();
        }
    }
}
