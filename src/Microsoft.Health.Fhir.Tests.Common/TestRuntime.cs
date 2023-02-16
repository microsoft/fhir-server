// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Tests.Common
{
    public static class TestRuntime
    {
        private const int MaxNumberOfAttempts = 3;

        public static void Run(Action action)
        {
            int currentExecution = 0;
            do
            {
                try
                {
                    action();
                    break;
                }
                catch (Exception ex) when (IsRetriableException(ex))
                {
                    currentExecution++;
                    if (currentExecution >= MaxNumberOfAttempts)
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

        public static async Task RunAsync(Func<Task> func)
        {
            int currentExecution = 0;
            do
            {
                try
                {
                    await func();
                    break;
                }
                catch (Exception ex) when (IsRetriableException(ex))
                {
                    currentExecution++;
                    if (currentExecution >= MaxNumberOfAttempts)
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

        private static bool IsRetriableException(Exception ex)
        {
            if (ex is HttpRequestException httpRequestException)
            {
                if (httpRequestException.InnerException is IOException ioException)
                {
                    if (ioException.InnerException is SocketException socketException)
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
                }
            }

            return false;
        }
    }
}
