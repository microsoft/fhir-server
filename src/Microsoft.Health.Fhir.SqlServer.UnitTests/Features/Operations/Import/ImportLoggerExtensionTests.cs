// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import;
using Microsoft.Health.JobManagement;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Operations.Import
{
    public sealed class ImportLoggerExtensionTests
    {
        [Fact]
        public void X()
        {
            const string expectedString = "[GroupId:2112/JobId:999] Message: test.";
            ILogger<ImportLoggerExtensionTests> logger = new CustomerTestingLogger(expectedString);

            JobInfo info = new JobInfo()
            {
                GroupId = 2112,
                Id = 999,
            };

            logger.LogJobInformation(info, "Message: {message}.", "test");
        }

        private sealed class CustomerTestingLogger : ILogger<ImportLoggerExtensionTests>
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
