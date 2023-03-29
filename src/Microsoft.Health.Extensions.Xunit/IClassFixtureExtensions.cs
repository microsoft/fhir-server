// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit
{
    public static class IClassFixtureExtensions
    {
        private const int MaxNumberOfAttempts = 3;

        public static void Retry<T>(this IClassFixture<T> fixture, Action action)
            where T : class
        {
            Retry(fixture, action, additionalRetriableExceptions: null);
        }

        public static void Retry<T>(this IClassFixture<T> fixture, Action action, HashSet<Type> additionalRetriableExceptions = null)
            where T : class
        {
            int currentExecution = 0;
            do
            {
                try
                {
                    action();
                    break;
                }
                catch (Exception ex) when (IsRetriableException(ex, additionalRetriableExceptions))
                {
                    currentExecution++;
                    if (currentExecution <= MaxNumberOfAttempts)
                    {
                        continue;
                    }

                    throw;
                }
                catch (Exception)
                {
                    throw;
                }
            }
            while (true);
        }

        public static async Task RetryAsync<T>(this IClassFixture<T> fixture, Func<Task> func)
            where T : class
        {
            await RetryAsync<T>(fixture, func, additionalRetriableExceptions: null);
        }

        public static async Task RetryAsync<T>(this IClassFixture<T> fixture, Func<Task> func, HashSet<Type> additionalRetriableExceptions = null)
            where T : class
        {
            int currentExecution = 0;
            do
            {
                try
                {
                    await func();
                    break;
                }
                catch (Exception ex) when (IsRetriableException(ex, additionalRetriableExceptions))
                {
                    currentExecution++;
                    if (currentExecution <= MaxNumberOfAttempts)
                    {
                        continue;
                    }

                    throw;
                }
                catch (Exception)
                {
                    throw;
                }
            }
            while (true);
        }

        private static bool IsRetriableException(Exception ex, HashSet<Type> additionalRetriableExceptions)
        {
            if (ex == null)
            {
                return false;
            }

            Exception targetException = ex;

            if (targetException is AggregateException aex)
            {
                return IsRetriableException(aex.InnerException, additionalRetriableExceptions);
            }
            else
            {
                if (additionalRetriableExceptions != null)
                {
                    if (additionalRetriableExceptions.Contains(targetException.GetType()))
                    {
                        return true;
                    }
                }

                if (targetException is SocketException socketException)
                {
                    if (socketException.Message == "An existing connection was forcibly closed by the remote host.")
                    {
                        return true;
                    }

                    SocketError[] retriableSocketErrors = new SocketError[]
                    {
                        SocketError.ConnectionAborted,
                        SocketError.ConnectionRefused,
                        SocketError.ConnectionReset,
                        SocketError.TryAgain,
                    };
                    if (retriableSocketErrors.Contains(socketException.SocketErrorCode))
                    {
                        return true;
                    }
                }

                if (targetException.InnerException != null)
                {
                    return IsRetriableException(targetException.InnerException, additionalRetriableExceptions);
                }

                return false;
            }
        }
    }
}
