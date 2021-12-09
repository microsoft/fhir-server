// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch
{
    internal abstract class AbstractPatchService<T>
    {
        #pragma warning disable SA1401
        protected IModelInfoProvider _modelInfoProvider;
        protected ISet<string> _immutableProperties;

        #pragma warning restore SA1401
        public AbstractPatchService(IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _modelInfoProvider = modelInfoProvider;
            _immutableProperties = new HashSet<string>
            {
                "Resource.id",
                "Resource.meta.lastUpdated",
                "Resource.meta.versionId",
                "Resource.text.div",
                "Resource.text.status",
            };
        }

        public abstract ResourceElement Patch(ResourceWrapper resourceToPatch, T paramsResource, WeakETag weakETag);

        protected abstract Resource GetPatchedJsonResource(FhirJsonNode node, T operations);
    }
}
