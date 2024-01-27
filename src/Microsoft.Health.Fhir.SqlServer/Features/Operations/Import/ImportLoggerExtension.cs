// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    internal static class ImportLoggerExtension
    {
        public static void LogJobInformation<T>(this ILogger<T> logger, JobInfo jobInfo, string message, params object[] args)
        {
            logger.LogJobInformation(exception: null, jobInfo, message, args);
        }

        public static void LogJobInformation<T>(this ILogger<T> logger, Exception exception, JobInfo jobInfo, string message, params object[] args)
        {
            // Combine prefix and message.
            string fullMessage = "[GroupId:{GroupId}/JobId:{JobId}] " + message;

            // Combine arguments.
            object[] finalArgs = new object[] { jobInfo.GroupId, jobInfo.Id };
            if (args != null)
            {
                finalArgs = finalArgs.Append(args).ToArray();
            }

            // Log exception if provided.
            if (exception != null)
            {
                logger.LogInformation(exception, fullMessage, finalArgs);
            }
            else
            {
                logger.LogInformation(fullMessage, finalArgs);
            }
        }
    }
}
