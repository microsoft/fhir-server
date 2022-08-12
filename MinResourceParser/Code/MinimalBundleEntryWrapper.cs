// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;

namespace MinResourceParser.Code
{
    public class MinimalBundleEntryWrapper
    {
        private readonly Lazy<ITypedElement> _entry;

        internal MinimalBundleEntryWrapper(ITypedElement entry)
        {
            EnsureArg.IsNotNull(entry, nameof(entry));

#pragma warning disable CS8603 // Possible null reference return.
            _entry = new Lazy<ITypedElement>(() => entry.Select("resource").FirstOrDefault());
#pragma warning restore CS8603 // Possible null reference return.
        }

        public ITypedElement Resource
        {
            get => _entry.Value;
        }
    }
}
