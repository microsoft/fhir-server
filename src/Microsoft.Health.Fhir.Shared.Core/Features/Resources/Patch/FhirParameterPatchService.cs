// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.FhirPath;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.JsonPatch.Exceptions;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch
{
    internal sealed class FhirParameterPatchService
    {
        private readonly IModelInfoProvider _modelInfoProvider;

        private readonly ISet<string> _immutableProperties = new HashSet<string>
        {
            "Resource.id",
            "Resource.meta.lastUpdated",
            "Resource.meta.versionId",
            "Resource.text.div",
            "Resource.text.status",
        };

        public FhirParameterPatchService(IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _modelInfoProvider = modelInfoProvider;
        }

        public ResourceElement Patch(ResourceWrapper resourceToPatch, Parameters paramsResource, WeakETag weakETag)
        {
            EnsureArg.IsNotNull(resourceToPatch, nameof(resourceToPatch));

            Validate(resourceToPatch, weakETag, paramsResource);

            var node = (FhirJsonNode)FhirJsonNode.Parse(resourceToPatch.RawResource.Data);

            // Capture the state of properties that are immutable
            ITypedElement resource = node.ToTypedElement(_modelInfoProvider.StructureDefinitionSummaryProvider);
            (string path, object result)[] preState = _immutableProperties.Select(x => (path: x, result: resource.Scalar(x))).ToArray();

            Resource patchedResource = paramsResource;

            (string path, object result)[] postState = _immutableProperties.Select(x => (path: x, result: resource.Scalar(x))).ToArray();
            if (!preState.Zip(postState).All(x => x.First.path == x.Second.path && string.Equals(x.First.result?.ToString(), x.Second.result?.ToString(), StringComparison.Ordinal)))
            {
                throw new RequestNotValidException(Core.Resources.PatchImmutablePropertiesIsNotValid);
            }

            return patchedResource.ToResourceElement();
        }

        private static void Validate(ResourceWrapper currentDoc, WeakETag eTag, Parameters paramsResource)
        {
            if (currentDoc.IsHistory)
            {
                throw new MethodNotAllowedException(Core.Resources.PatchVersionNotAllowed);
            }

            if (eTag != null && eTag.VersionId != currentDoc.Version)
            {
                throw new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, eTag.VersionId));
            }
            var context = new System.ComponentModel.DataAnnotations.ValidationContext(currentDoc);
            var results = paramsResource.Validate(context);
            foreach(var result in results)
            {
                if(result.ErrorMessage != null)
                {
                    throw new BadRequestException($"{result.MemberNames} is invalid.");
                }
            }
        }

    }
}
