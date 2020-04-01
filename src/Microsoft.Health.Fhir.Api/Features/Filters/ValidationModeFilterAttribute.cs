// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Class)]
    internal class ValidationModeFilterAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            StringValues modes;
            if (context.HttpContext.Request.Query.TryGetValue(KnownQueryParameterNames.Mode, out modes) && modes.Count > 0)
            {
                if (modes.Count > 1)
                {
                    throw new BadRequestException(Resources.MultipleModesProvided);
                }

                var idMode = context.ActionDescriptor.DisplayName.Contains("ValidateById", StringComparison.OrdinalIgnoreCase);
                var mode = modes[0];
                ParseMode(mode, idMode);
            }
        }

        public static void ParseMode(string mode, bool idMode)
        {
            switch (mode == null ? null : mode.ToUpperInvariant())
            {
                case "CREATE":
                    throw new OperationNotImplementedException(string.Format(Resources.ValidationModeNotSupported, mode));
                case "UPDATE":
                    if (idMode)
                    {
                        throw new OperationNotImplementedException(string.Format(Resources.ValidationModeNotSupported, mode));
                    }

                    throw new BadRequestException(Resources.ValidationForUpdateAndDeleteNotSupported);
                case "DELETE":
                    // When a resource is requested to be deleted and an id is provided no validation is done as we do not currenty check that the id exists. 
                    // A validation passed message is returned without looking at the message body.
                    if (idMode)
                    {
                        // This is done to bypass any exceptions that could be thrown in latter attribute filters.
                        throw new ResourceNotValidException(new List<OperationOutcomeIssue>
                        {
                            new OperationOutcomeIssue(
                                OperationOutcomeConstants.IssueSeverity.Information,
                                OperationOutcomeConstants.IssueType.Informational,
                                Core.Resources.ValidationPassed),
                        });
                    }

                    throw new BadRequestException(Resources.ValidationForUpdateAndDeleteNotSupported);
                case "":
                case null:
                    break;
                default:
                    throw new BadRequestException(string.Format(Resources.ValidationModeNotRecognized, mode));
            }
        }
    }
}
