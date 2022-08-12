// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Models;

namespace MinResourceParser.Code
{
    public class MinimalBundleWrapper
    {
        private Lazy<IReadOnlyList<MinimalBundleEntryWrapper>> _entries;

        public MinimalBundleWrapper(ITypedElement bundle)
        {
            EnsureArg.IsNotNull(bundle, nameof(bundle));
            EnsureArg.Is(KnownResourceTypes.Bundle, bundle.InstanceType, StringComparison.Ordinal, nameof(bundle));

            _entries = new Lazy<IReadOnlyList<MinimalBundleEntryWrapper>>(() => bundle.Select("entry").Select(x => new MinimalBundleEntryWrapper(x)).ToArray());
        }

        public IReadOnlyList<MinimalBundleEntryWrapper> Entries => _entries.Value;
    }
}
