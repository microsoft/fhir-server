// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Audit
{
    public class Log
    {
        public Log(LogLevel logLevel, string state, Exception exception)
        {
            LogLevel = logLevel;
            State = state;
            Exception = exception;
        }

        public Exception Exception { get; private set; }

        public LogLevel LogLevel { get; private set; }

        public string State { get; private set; }
    }
}
