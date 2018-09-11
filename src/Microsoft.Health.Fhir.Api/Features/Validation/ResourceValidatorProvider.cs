// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace Microsoft.Health.Fhir.Api.Features.Validation
{
    public class ResourceValidatorProvider : IModelValidatorProvider
    {
        private readonly ResourceValidator _resourceValidator;

        public ResourceValidatorProvider()
        {
            _resourceValidator = new ResourceValidator();
        }

        public void CreateValidators(ModelValidatorProviderContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            if (typeof(Resource).IsAssignableFrom(context.ModelMetadata.ModelType))
            {
                context.Results.Add(new ValidatorItem { Validator = _resourceValidator });
            }
        }
    }
}
