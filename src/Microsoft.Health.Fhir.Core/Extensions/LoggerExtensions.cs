#nullable enable

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Parameters;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class LoggerExtensions
    {
#pragma warning disable CA1068 // CancellationToken parameters must come last. Cancellation token being last breaks up the readability of the command by seperating the message and its args.
        public static void LogVerbose(this ILogger logger, IParameterStore store, CancellationToken cancellationToken, string message, params object?[] args)
#pragma warning restore CA1068 // CancellationToken parameters must come last
        {
            var logParameterTask = store.GetParameter("LogLevel", cancellationToken);

            while (logParameterTask.Status != TaskStatus.RanToCompletion)
            {
                if (logParameterTask.Status == TaskStatus.Faulted)
                {
                    logger.LogWarning(logParameterTask.Exception, "Failed to retrieve LogLevel parameter. Logging at default level.");
                    return;
                }
                else if (logParameterTask.Status == TaskStatus.Canceled)
                {
                    logger.LogWarning("Retrieving LogLevel parameter cancelled. Logging at default level.");
                    return;
                }

                Thread.Sleep(5);
            }

#pragma warning disable CA1849 // Runs synchronously to allow for the logger to be used in a synchronous context.
            if (logParameterTask.Result.CharValue == "Verbose")
#pragma warning restore CA1068
            {
                logger.LogInformation(message, args);
            }
        }
    }
}
