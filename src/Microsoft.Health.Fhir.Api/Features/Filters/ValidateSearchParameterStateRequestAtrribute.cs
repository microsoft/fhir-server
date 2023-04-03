// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Core.Features.Operations.SearchParameterState;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    /// <summary>
    /// A filter that validates the query string for a SearchParameter $Status search request
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class ValidateSearchParameterStateRequestAtrribute : ActionFilterAttribute
    {
        private static Dictionary<string, HashSet<string>> _supportedParams = InitSupportedParams();

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));
            var queryKeys = context.HttpContext.Request.Query.Keys;

            foreach (string key in queryKeys)
            {
                if (!_supportedParams[context.HttpContext.Request.Method].Contains(key))
                {
                    throw new BadRequestException(string.Format(Core.Resources.UnsupportedSearchParameterStateQueryParameter, key));
                }
            }
        }

        private static Dictionary<string, HashSet<string>> InitSupportedParams()
        {
            var getParams = new HashSet<string>()
            {
                SearchParameterStateProperties.Code,
                SearchParameterStateProperties.Url,
                SearchParameterStateProperties.ResourceType,
                SearchParameterStateProperties.SearchParameterId,
            };
            var postParams = new HashSet<string>()
            {
                SearchParameterStateProperties.Code,
                SearchParameterStateProperties.Url,
                SearchParameterStateProperties.ResourceType,
            };

            var putParams = new HashSet<string>()
            {
                SearchParameterStateProperties.Url,
                SearchParameterStateProperties.Status,
            };

            var supportedParams = new Dictionary<string, HashSet<string>>
            {
                { HttpMethods.Get, getParams },
                { HttpMethods.Post, postParams },
                { HttpMethods.Put, putParams },
            };

            return supportedParams;
        }
    }
}
