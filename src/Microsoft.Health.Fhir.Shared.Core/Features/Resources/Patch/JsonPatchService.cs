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
    internal sealed class JsonPatchService
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

        public JsonPatchService(IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _modelInfoProvider = modelInfoProvider;
        }

        public ResourceElement Patch(ResourceWrapper resourceToPatch, JsonPatchDocument patchDocument, WeakETag weakETag)
        {
            EnsureArg.IsNotNull(resourceToPatch, nameof(resourceToPatch));

            Validate(resourceToPatch, weakETag, patchDocument);

            var node = (FhirJsonNode)FhirJsonNode.Parse(resourceToPatch.RawResource.Data);

            // Capture the state of properties that are immutable
            ITypedElement resource = node.ToTypedElement(_modelInfoProvider.StructureDefinitionSummaryProvider);
            (string path, object result)[] preState = _immutableProperties.Select(x => (path: x, result: resource.Scalar(x))).ToArray();

            Resource patchedResource = GetPatchedJsonResource(node, patchDocument);

            (string path, object result)[] postState = _immutableProperties.Select(x => (path: x, result: resource.Scalar(x))).ToArray();
            if (!preState.Zip(postState).All(x => x.First.path == x.Second.path && string.Equals(x.First.result?.ToString(), x.Second.result?.ToString(), StringComparison.Ordinal)))
            {
                throw new RequestNotValidException(Core.Resources.PatchImmutablePropertiesIsNotValid);
            }

            return patchedResource.ToResourceElement();
        }

        private static void Validate(ResourceWrapper currentDoc, WeakETag eTag, JsonPatchDocument patchDocument)
        {
            if (currentDoc.IsHistory)
            {
                throw new MethodNotAllowedException(Core.Resources.PatchVersionNotAllowed);
            }

            if (eTag != null && eTag.VersionId != currentDoc.Version)
            {
                throw new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, eTag.VersionId));
            }

            foreach (var operation in patchDocument.Operations)
            {
                if (operation.OperationType == AspNetCore.JsonPatch.Operations.OperationType.Invalid)
                {
                    throw new BadRequestException($"{operation.op} is invalid.");
                }
            }
        }

        private Resource GetPatchedJsonResource(FhirJsonNode node, JsonPatchDocument operations)
        {
            try
            {
                operations.ApplyTo(node.JsonObject);
            }
            catch (JsonPatchException e)
            {
                throw new RequestNotValidException(e.Message, OperationOutcomeConstants.IssueType.Processing);
            }
            catch (ArgumentNullException e)
            {
                throw new RequestNotValidException(e.Message, OperationOutcomeConstants.IssueType.Processing);
            }

            Resource resourcePoco;
            try
            {
                var resource = node.ToTypedElement(_modelInfoProvider.StructureDefinitionSummaryProvider);
                resourcePoco = resource.ToPoco<Resource>();
            }
            catch (Exception e)
            {
                throw new RequestNotValidException(string.Format(Core.Resources.PatchResourceError, e.Message));
            }

            return resourcePoco;
        }
    }
}
