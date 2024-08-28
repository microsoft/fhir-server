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
        private readonly bool _isDeleted;

        public RawBundleEntryComponent(ResourceWrapper resourceWrapper)
        {
            EnsureArg.IsNotNull(resourceWrapper, nameof(resourceWrapper));

            ResourceElement = new RawResourceElement(resourceWrapper);
            _isDeleted = resourceWrapper.IsDeleted;
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

        /// <summary>
        /// Gets a value indicating whether or not the resource has been deleted.
        /// </summary>
        /// <returns>True if the resource is deleted.</returns>
        /// <remarks>This instance method supersedes the extension method, Bundle.EntryComponent.IsDeleted().</remarks>
        public bool IsDeleted()
        {
            return _isDeleted;
        }
    }
}
