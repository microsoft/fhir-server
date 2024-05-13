// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
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
        private readonly RawResourceElement _resourceElement;

        public RawBundleEntryComponent(ResourceWrapper resourceWrapper)
        {
            EnsureArg.IsNotNull(resourceWrapper, nameof(resourceWrapper));

            ResourceElement = new RawResourceElement(resourceWrapper);
        }

        public RawBundleEntryComponent(RawResourceElement rawResourceElement)
        {
            EnsureArg.IsNotNull(rawResourceElement, nameof(rawResourceElement));

            ResourceElement = rawResourceElement;
        }

        /// <summary>
        /// A RawBundleEntryComponent with no payload.
        /// </summary>
        public RawBundleEntryComponent()
        {
        }

        public RawResourceElement ResourceElement
        {
            get => _resourceElement;
            init
            {
                Debug.Assert(
                    !string.Equals(value?.InstanceType, KnownResourceTypes.OperationOutcome, StringComparison.Ordinal),
                    "OperationOutcome should not be set as a resource element.");

                _resourceElement = value;
            }
        }

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
