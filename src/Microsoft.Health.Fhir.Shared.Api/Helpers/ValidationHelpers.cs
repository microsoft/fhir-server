// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using FluentValidation.Results;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Validation;

namespace Microsoft.Health.Fhir.Api.Helpers
{
    internal class ValidationHelpers
    {
        public static void ValidateId(Resource resource, string expectedId)
        {
            var location = $"{resource.TypeName}.id";
            if (string.IsNullOrWhiteSpace(resource.Id))
            {
                throw new ResourceNotValidException(new List<ValidationFailure>
                    {
                        new ValidationFailure(location, Api.Resources.ResourceIdRequired),
                    });
            }

            if (!string.Equals(expectedId, resource.Id, StringComparison.Ordinal))
            {
                throw new ResourceNotValidException(new List<ValidationFailure>
                    {
                        new ValidationFailure(location, Api.Resources.UrlResourceIdMismatch),
                    });
            }
        }

        public static void ValidateType(Resource resource, string expectedType)
        {
            if (!string.Equals(expectedType, resource.TypeName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ResourceNotValidException(new List<ValidationFailure>
                    {
                        new ValidationFailure(nameof(Base.TypeName), Api.Resources.ResourceTypeMismatch),
                    });
            }
        }
    }
}
