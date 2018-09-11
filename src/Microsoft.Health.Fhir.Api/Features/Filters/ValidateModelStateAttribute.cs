// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Core.Features.Validation;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Class)]
    public class ValidateModelStateAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            if (!context.ModelState.IsValid)
            {
                var validationErrors = context.ModelState
                    .SelectMany(x => x.Value.Errors.Select(error => new OperationOutcome.IssueComponent
                    {
                        Severity = OperationOutcome.IssueSeverity.Error,
                        Code = OperationOutcome.IssueType.Invalid,
                        Diagnostics = $"{QuoteField(x.Key)}{error.ErrorMessage}",
                    })).ToArray();

                throw new ResourceNotValidException(validationErrors);
            }
        }

        private static string QuoteField(string fieldName)
        {
            if (!string.IsNullOrEmpty(fieldName))
            {
                return $"'{fieldName}' ";
            }

            return fieldName;
        }
    }
}
