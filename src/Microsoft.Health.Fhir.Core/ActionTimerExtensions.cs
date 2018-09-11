// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core
{
    public static class ActionTimerExtensions
    {
        public static IDisposable BeginTimedScope(this ILogger logger, string scopeName)
        {
            return new ActionTimer(logger, scopeName);
        }
    }
}
