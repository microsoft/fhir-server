// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Class)]
    public class ValidateModelStateAttribute : ActionFilterAttribute
    {
        private static readonly string[] _contentMethods = { "POST", "PUT", "PATCH" };

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            if (!context.ModelState.IsValid && _contentMethods.Contains(context.HttpContext.Request.Method.ToUpperInvariant()))
            {
                var validationErrors = context.ModelState
                    .SelectMany(x => x.Value.Errors.Select(error => new OperationOutcomeIssue(
                        OperationOutcomeConstants.IssueSeverity.Error,
                        OperationOutcomeConstants.IssueType.Invalid,
                        $"{QuoteField(x.Key)}{error.ErrorMessage}"))).ToArray();

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
