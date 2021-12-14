// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
    public class JsonPatchService : BasePatchService<JsonPatchDocument>
    {
        public JsonPatchService(IModelInfoProvider modelInfoProvider)
        : base(modelInfoProvider)
        {
        }

        protected override void Validate(ResourceWrapper currentDoc, WeakETag eTag, JsonPatchDocument patchDocument)
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

        protected override Resource GetPatchedJsonResource(FhirJsonNode node, JsonPatchDocument operations)
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
                var resource = node.ToTypedElement(ModelInfoProvider.StructureDefinitionSummaryProvider);
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
