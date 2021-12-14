// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using FhirPathPatch;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch
{
    public class FhirParameterPatchService : BasePatchService<Parameters>
    {

        public FhirParameterPatchService(IModelInfoProvider modelInfoProvider) : base(modelInfoProvider) { }
        protected override void Validate(ResourceWrapper currentDoc, WeakETag eTag, Parameters paramsResource)
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

            foreach (var result in results)
            {
                if (result.ErrorMessage != null)
                {
                    throw new BadRequestException($"{result.MemberNames} is invalid.");
                }
            }
        }

        protected override Resource GetPatchedJsonResource(FhirJsonNode node, Parameters operations)
        {
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

            var builder = new FhirPathPatchBuilder(resourcePoco, operations);
            return builder.Apply();
        }
    }
}
