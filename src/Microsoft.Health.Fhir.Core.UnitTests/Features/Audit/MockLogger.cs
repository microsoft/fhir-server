// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Audit
{
    public class MockLogger<T> : IMockLogger<T>
    {
        private IList<Log> _logs;

        public MockLogger()
        {
            _logs = new List<Log>();
        }

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public IList<Log> GetLogs()
        {
            return _logs;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            _logs.Add(new Log(logLevel, formatter(state, exception), exception));
        }
    }
}
