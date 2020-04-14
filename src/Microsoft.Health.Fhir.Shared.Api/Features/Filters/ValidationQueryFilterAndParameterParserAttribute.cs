// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Class)]
    internal class ValidationQueryFilterAndParameterParserAttribute : ActionFilterAttribute
    {
        private readonly FeatureConfiguration _features;

        public ValidationQueryFilterAndParameterParserAttribute(IOptions<FeatureConfiguration> features)
        {
            EnsureArg.IsNotNull(features?.Value, nameof(features));

            _features = features?.Value;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            if (!_features.SupportsValidate)
            {
                throw new OperationNotImplementedException(Api.Resources.ValidationNotSupported);
            }

            var idMode = context.ActionDescriptor.DisplayName.Contains("ValidateById", StringComparison.OrdinalIgnoreCase);

            string mode = null;
            StringValues modes;
            if (context.HttpContext.Request.Query.TryGetValue(KnownQueryParameterNames.Mode, out modes) && modes.Count > 0)
            {
                if (modes.Count > 1)
                {
                    throw new BadRequestException(Api.Resources.MultipleModesProvided);
                }

                mode = modes[0];
            }

            string profile = null;
            StringValues profiles;
            if (context.HttpContext.Request.Query.TryGetValue(KnownQueryParameterNames.Profile, out profiles) && profiles.Count > 0)
            {
                if (profiles.Count > 1)
                {
                    throw new BadRequestException(Api.Resources.MultipleProfilesProvided);
                }

                profile = profiles[0];
            }

            if (context.ActionArguments.TryGetValue(KnownActionParameterNames.Resource, out var parsedModel))
            {
                if (((Resource)parsedModel).ResourceType == ResourceType.Parameters)
                {
                    ParseParameters((Parameters)parsedModel, ref profile, ref mode);
                }
            }

            ParseMode(mode, idMode);

            if (profile != null)
            {
                throw new OperationNotImplementedException(Api.Resources.ValidateWithProfileNotSupported);
            }
        }

        private static void ParseParameters(Parameters resource, ref string profile, ref string mode)
        {
            var paramMode = resource.Parameter.Find(param => param.Name.Equals("mode", System.StringComparison.OrdinalIgnoreCase));
            if (paramMode != null && mode != null)
            {
                throw new BadRequestException(Api.Resources.MultipleModesProvided);
            }
            else if (paramMode != null && mode == null)
            {
                mode = paramMode.Value.ToString();
            }

            var paramProfile = resource.Parameter.Find(param => param.Name.Equals("profile", System.StringComparison.OrdinalIgnoreCase));
            if (paramProfile != null && profile != null)
            {
                throw new BadRequestException(Api.Resources.MultipleProfilesProvided);
            }
            else if (paramProfile != null && profile == null)
            {
                profile = paramProfile.Value.ToString();
            }
        }

        private static void ParseMode(string mode, bool idMode)
        {
            switch (mode == null ? null : mode.ToUpperInvariant())
            {
                case "CREATE":
                    throw new OperationNotImplementedException(string.Format(Api.Resources.ValidationModeNotSupported, mode));
                case "UPDATE":
                    if (idMode)
                    {
                        throw new OperationNotImplementedException(string.Format(Api.Resources.ValidationModeNotSupported, mode));
                    }

                    throw new BadRequestException(Api.Resources.ValidationForUpdateAndDeleteNotSupported);
                case "DELETE":
                    // When a resource is requested to be deleted and an id is provided no validation is done as we do not currenty check that the id exists.
                    // A validation passed message is returned without looking at the message body.
                    if (idMode)
                    {
                        // This is done to bypass any exceptions that could be thrown in latter attribute filters.
                        throw new ResourceNotValidException(new List<OperationOutcomeIssue>
                        {
                            ValidateOperationHandler.ValidationPassed,
                        });
                    }

                    throw new BadRequestException(Api.Resources.ValidationForUpdateAndDeleteNotSupported);
                case "":
                case null:
                    break;
                default:
                    throw new BadRequestException(string.Format(Api.Resources.ValidationModeNotRecognized, mode));
            }
        }
    }
}
