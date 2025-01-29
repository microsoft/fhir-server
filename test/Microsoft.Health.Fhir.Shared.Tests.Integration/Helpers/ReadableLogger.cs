// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Tests.Integration
{
    public class ReadableLogger<T> : ILogger<T>
    {
        private List<string> _logs = new List<string>();

        public IDisposable BeginScope<TState>(TState state)
        {
            throw new NotImplementedException();
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            throw new NotImplementedException();
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            _logs.Add(formatter(state, exception));
        }

        public void LogError(string message)
        {
            _logs.Add(message);
        }

        public void LogInformation(string message)
        {
            _logs.Add(message);
        }

        public void LogWarning(string message)
        {
            _logs.Add(message);
        }

        public bool TryGetLatestLog(string content, out string match)
        {
            if (_logs.Any(l => l.Contains(content)))
            {
                match = _logs.FindLast(l => l.Contains(content));
                return true;
            }

            match = null;
            return false;
        }
    }
}
