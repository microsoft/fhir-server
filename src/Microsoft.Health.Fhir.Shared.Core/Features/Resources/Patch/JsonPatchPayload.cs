// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.JsonPatch.Exceptions;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch
{
    public class JsonPatchPayload : PatchPayload
    {
        public JsonPatchPayload(JsonPatchDocument patchDocument)
        {
            EnsureArg.IsNotNull(patchDocument, nameof(patchDocument));
            PatchDocument = patchDocument;
            Validate();
        }

        public JsonPatchDocument PatchDocument { get; }

        internal void Validate()
        {
            foreach (var operation in PatchDocument.Operations)
            {
                if (operation.OperationType == AspNetCore.JsonPatch.Operations.OperationType.Invalid)
                {
                    throw new BadRequestException($"{operation.op} is invalid.");
                }
            }
        }

        internal override ResourceElement GetPatchedResourceElement(ResourceWrapper resourceToPatch)
        {
            var resourceJsonNode = (FhirJsonNode)FhirJsonNode.Parse(resourceToPatch.RawResource.Data);

            try
            {
                PatchDocument.ApplyTo(resourceJsonNode.JsonObject);
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
                resourcePoco = resourceJsonNode
                    .ToTypedElement(ModelInfoProvider.StructureDefinitionSummaryProvider)
                    .ToPoco<Resource>();
            }
            catch (Exception e)
            {
                throw new RequestNotValidException(string.Format(Core.Resources.PatchResourceError, e.Message));
            }

            return resourcePoco.ToResourceElement();
        }
    }
}
