// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using FhirPathPatch;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch
{
    public class FhirParameterPatchPayload : PatchPayload
    {
        public FhirParameterPatchPayload(Parameters fhirPatchParameters)
        {
            EnsureArg.IsNotNull(fhirPatchParameters, nameof(fhirPatchParameters));
            FhirPatchParameters = fhirPatchParameters;
        }

        public Parameters FhirPatchParameters { get; }

        internal override void Validate(ResourceWrapper currentDoc, WeakETag eTag)
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
            var results = FhirPatchParameters.Validate(context);

            foreach (var result in results)
            {
                if (result.ErrorMessage != null)
                {
                    throw new BadRequestException($"{result.MemberNames} is invalid.");
                }
            }
        }

        internal override Resource GetPatchedJsonResource(FhirJsonNode node)
        {
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

            try
            {
                return new FhirPathPatchBuilder(resourcePoco, FhirPatchParameters).Apply();
            }
            catch (InvalidOperationException e)
            {
                // Invalid patch input
                throw new RequestNotValidException(e.Message);
            }
            catch (FormatException e)
            {
                // Invalid output POCO
                throw new RequestNotValidException(e.Message);
            }
        }
    }
}
