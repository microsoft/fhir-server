// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Operations;
using Microsoft.Health.JobManagement;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Operations
{
    public sealed class JobLoggerExtensionTests
    {
        [Fact]
        public void WhenLoggingAJobInformation_PreFixLoggWithJobInformation_AndLogOneParameter()
        {
            const string expectedString = "[GroupId:2112/JobId:999] Message: test.";
            ILogger<JobLoggerExtensionTests> logger = new CustomerTestingLogger(expectedString);

            var info = new JobInfo()
            {
                GroupId = 2112,
                Id = 999,
            };

            logger.LogJobInformation(info, "Message: {message}.", "test");
        }

        [Fact]
        public void WhenLoggingAJobInformation_PreFixLoggWithJobInformation_AndLogThreeParameters()
        {
            const string expectedString = "[GroupId:1234/JobId:4321] Param1: A / Param2: b / Param3: C3.";
            ILogger<JobLoggerExtensionTests> logger = new CustomerTestingLogger(expectedString);

            var info = new JobInfo()
            {
                GroupId = 1234,
                Id = 4321,
            };

            logger.LogJobInformation(info, "Param1: {param1} / Param2: {param2} / Param3: {param3}.", "A", "b", "C3");
        }

        [Fact]
        public void WhenLoggingAJobInformation_PreFixLoggWithJobInformation_AndLogFiveParameters()
        {
            const string expectedString = "[GroupId:222/JobId:333] Param1: 'A' / Param2: B / Param3: 3 / Param4: 'D' / Param5: 55.";
            ILogger<JobLoggerExtensionTests> logger = new CustomerTestingLogger(expectedString);

            var info = new JobInfo()
            {
                GroupId = 222,
                Id = 333,
            };

            logger.LogJobInformation(info, "Param1: '{P1}' / Param2: {P2} / Param3: {P3} / Param4: '{P4}' / Param5: {P5}.", "A", "B", 3, "D", 55);
        }

        private sealed class CustomerTestingLogger : ILogger<JobLoggerExtensionTests>
        {
            private readonly Stream _stream = new MemoryStream();
            private readonly string _expectedString;

            public CustomerTestingLogger(string expectedString)
            {
                _expectedString = expectedString;
            }

            public IDisposable BeginScope<TState>(TState state)
            {
                return _stream;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                string result = formatter(state, exception);

                Assert.Equal(_expectedString, result);
            }
        }
    }
}
