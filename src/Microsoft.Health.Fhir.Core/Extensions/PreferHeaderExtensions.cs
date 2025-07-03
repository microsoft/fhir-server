// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class PreferHeaderExtensions
    {
        internal static bool GetIsStrictHandlingEnabled(this RequestContextAccessor<IFhirRequestContext> contextAccessor)
        {
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));

            return GetHandlingHeader(contextAccessor) == SearchParameterHandling.Strict;
        }

        internal static SearchParameterHandling? GetHandlingHeader(this RequestContextAccessor<IFhirRequestContext> contextAccessor)
        {
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));

            if (contextAccessor.RequestContext?.RequestHeaders != null &&
                contextAccessor.RequestContext.RequestHeaders.TryGetValue(KnownHeaders.Prefer, out StringValues values))
            {
                var handlingValue = values.SelectMany(x => x.Split(',', StringSplitOptions.TrimEntries)).FirstOrDefault(x => x.StartsWith("handling=", StringComparison.OrdinalIgnoreCase));
                if (handlingValue != default)
                {
                    handlingValue = handlingValue.Substring("handling=".Length);

                    if (string.IsNullOrWhiteSpace(handlingValue) ||
                        !Enum.TryParse(handlingValue, true, out SearchParameterHandling handling))
                    {
                        throw new BadRequestException(string.Format(
                            Resources.InvalidHandlingValue,
                            handlingValue,
                            string.Join(",", Enum.GetNames<SearchParameterHandling>())));
                    }

                    return handling;
                }
            }

            return null;
        }

        internal static ReturnPreference? GetReturnPreferenceValue(this RequestContextAccessor<IFhirRequestContext> contextAccessor)
        {
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));

            if (contextAccessor.RequestContext?.RequestHeaders != null &&
                contextAccessor.RequestContext.RequestHeaders.TryGetValue(KnownHeaders.Prefer, out StringValues values))
            {
                var returnValue = values.SelectMany(x => x.Split(',', StringSplitOptions.TrimEntries)).FirstOrDefault(x => x.StartsWith("return=", StringComparison.OrdinalIgnoreCase));
                if (returnValue?.Any() ?? false)
                {
                    returnValue = returnValue.Substring("return=".Length);

                    if (string.IsNullOrWhiteSpace(returnValue) ||
                        !Enum.TryParse(returnValue, true, out ReturnPreference returnPreference))
                    {
                        throw new BadRequestException(string.Format(
                            Resources.InvalidReturnPreferenceValue,
                            returnValue,
                            string.Join(",", Enum.GetNames<ReturnPreference>())));
                    }

                    return returnPreference;
                }
            }

            return null;
        }
    }
}
