// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;

namespace Microsoft.Health.Fhir.Core.Features.Definition.BundleNavigators
{
    internal class BundleWrapper
    {
        private readonly ITypedElement _bundle;

        public BundleWrapper(ITypedElement bundle)
        {
            EnsureArg.IsNotNull(bundle, nameof(bundle));

            _bundle = bundle;
        }

        public IReadOnlyList<BundleEntryWrapper> GetEntries()
        {
            return _bundle.Select("entry").Select(x => new BundleEntryWrapper(x)).ToArray();
        }
    }
}
