#nullable enable

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Parameters;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class LoggerExtensions
    {
        public static void LogVerbose(this ILogger logger, string message, IParameterStore store, params object?[] args)
        {
            if (store.GetParameter("LogLevel").CharValue == "Verbose")
            {
                logger.LogInformation(message, args);
            }
        }
    }
}
