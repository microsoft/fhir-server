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

            bool isStrictHandlingEnabled = false;

            if (contextAccessor.RequestContext?.RequestHeaders != null &&
                contextAccessor.RequestContext.RequestHeaders.TryGetValue(KnownHeaders.Prefer, out StringValues values))
            {
                var handlingValue = values.FirstOrDefault(x => x.StartsWith("handling=", StringComparison.OrdinalIgnoreCase));
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

                    if (handling == SearchParameterHandling.Strict)
                    {
                        isStrictHandlingEnabled = true;
                    }
                }
            }

            return isStrictHandlingEnabled;
        }
    }
}
