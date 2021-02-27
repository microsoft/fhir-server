// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using Azure;
using Microsoft.Health.Fhir.Azure.ExportDestinationClient;
using Xunit;

namespace Microsoft.Health.Fhir.Azure.UnitTests.ExportDestinationClient
{
    public class StorageExceptionParserTests
    {
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.NotFound)]
        [Theory]
        public void GivenValidErrorStatusCode_WhenParseStorageException_ThenCorrespondingHttpStatusCode(HttpStatusCode statusCode)
        {
            var ex = new RequestFailedException((int)statusCode, "exception message", new Exception("inner exception message"));

            var resultCode = StorageExceptionParser.ParseStorageException(ex);

            Assert.Equal(statusCode, resultCode);
        }

        [InlineData(0)]
        [InlineData(1)]
        [InlineData(777)]
        [Theory]
        public void GivenInValidStatusCode_WhenParseStorageException_ThenReturnsInternalServerError(int statusCode)
        {
            var ex = new RequestFailedException(statusCode, "exception message", new Exception("inner exception message"));

            var resultCode = StorageExceptionParser.ParseStorageException(ex);

            Assert.Equal(HttpStatusCode.InternalServerError, resultCode);
        }

        [Fact]
        public void GivenNoRequestInformationAndUnknownHostMessage_WhenParseStorageException_ThenReturnsBadRequest()
        {
            var ex = new RequestFailedException("No such host is known");

            var resultCode = StorageExceptionParser.ParseStorageException(ex);

            Assert.Equal(HttpStatusCode.BadRequest, resultCode);
        }
    }
}
