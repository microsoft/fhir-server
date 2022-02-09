// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch
{
    public class FhirPathPatchPayload : PatchPayload
    {
        public FhirPathPatchPayload(Parameters fhirPatchParameters)
        {
            EnsureArg.IsNotNull(fhirPatchParameters, nameof(fhirPatchParameters));
            FhirPatchParameters = fhirPatchParameters;
        }

        public Parameters FhirPatchParameters { get; }

        internal override ResourceElement GetPatchedResourceElement(ResourceWrapper resourceToPatch)
        {
            Resource resourcePoco;

            try
            {
                resourcePoco = resourceToPatch.RawResource
                    .ToITypedElement(ModelInfoProvider.Instance)
                    .ToPoco<Resource>();
            }
            catch (Exception e)
            {
                throw new RequestNotValidException(string.Format(Core.Resources.PatchResourceError, e.Message));
            }

            try
            {
                Resource patchedResource = new FhirPathPatchBuilder(resourcePoco, FhirPatchParameters).Apply();
                return patchedResource.ToResourceElement();
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
