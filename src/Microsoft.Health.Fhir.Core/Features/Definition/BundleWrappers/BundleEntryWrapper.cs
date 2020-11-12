// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;

namespace Microsoft.Health.Fhir.Core.Features.Definition.BundleWrappers
{
    public class BundleEntryWrapper
    {
        private readonly Lazy<ITypedElement> _entry;

        public BundleEntryWrapper(ITypedElement entry)
        {
            EnsureArg.IsNotNull(entry, nameof(entry));

            _entry = new Lazy<ITypedElement>(() => entry.Select("resource").FirstOrDefault());
        }

        public ITypedElement Resource
        {
            get => _entry.Value;
        }
    }
}
