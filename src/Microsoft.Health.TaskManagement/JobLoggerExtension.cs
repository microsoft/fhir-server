// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.JobManagement
{
    public static class JobLoggerExtension
    {
        public static void LogJobInformation<T>(this ILogger<T> logger, JobInfo jobInfo, string message, params object[] args)
        {
            LogInformation(logType: 1, logger, exception: null, jobInfo, message, args);
        }

        public static void LogJobInformation<T>(this ILogger<T> logger, Exception exception, JobInfo jobInfo, string message, params object[] args)
        {
            LogInformation(logType: 1, logger, exception, jobInfo, message, args);
        }

        public static void LogJobError<T>(this ILogger<T> logger, JobInfo jobInfo, string message, params object[] args)
        {
            LogInformation(logType: 2, logger, exception: null, jobInfo, message, args);
        }

        public static void LogJobError<T>(this ILogger<T> logger, Exception exception, JobInfo jobInfo, string message, params object[] args)
        {
            LogInformation(logType: 2, logger, exception, jobInfo, message, args);
        }

        public static void LogJobWarning<T>(this ILogger<T> logger, JobInfo jobInfo, string message, params object[] args)
        {
            LogInformation(logType: 3, logger, exception: null, jobInfo, message, args);
        }

        public static void LogJobWarning<T>(this ILogger<T> logger, Exception exception, JobInfo jobInfo, string message, params object[] args)
        {
            LogInformation(logType: 3, logger, exception, jobInfo, message, args);
        }

        private static void LogInformation<T>(int logType, ILogger<T> logger, Exception exception, JobInfo jobInfo, string message, params object[] args)
        {
            // Combine prefix and message.
            string fullMessage = "[GroupId:{GroupId}/JobId:{JobId}] " + message;

            // Combine arguments.
            List<object> finalArgs = new List<object>();
            finalArgs.Add(jobInfo?.GroupId);
            finalArgs.Add(jobInfo?.Id);
            if (args != null)
            {
                foreach (object messageArgument in args)
                {
                    finalArgs.Add(messageArgument);
                }
            }

            if (logType == 1) // Information
            {
                // Log exception if provided.
                if (exception != null)
                {
                    logger.LogInformation(exception, fullMessage, finalArgs.ToArray());
                }
                else
                {
                    logger.LogInformation(fullMessage, finalArgs.ToArray());
                }
            }
            else if (logType == 2) // Error
            {
                // Log exception if provided.
                if (exception != null)
                {
                    logger.LogError(exception, fullMessage, finalArgs.ToArray());
                }
                else
                {
                    logger.LogError(fullMessage, finalArgs.ToArray());
                }
            }
            else if (logType == 3) // Warning
            {
                // Log exception if provided.
                if (exception != null)
                {
                    logger.LogWarning(exception, fullMessage, finalArgs.ToArray());
                }
                else
                {
                    logger.LogWarning(fullMessage, finalArgs.ToArray());
                }
            }
            else
            {
                throw new InvalidOperationException($"Invalid LogType '{logType}'.");
            }
        }
    }
}
