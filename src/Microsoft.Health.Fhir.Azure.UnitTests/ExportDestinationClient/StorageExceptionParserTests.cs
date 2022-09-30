// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using Microsoft.Azure.Storage;
using Microsoft.Health.Fhir.Azure.ExportDestinationClient;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Azure.UnitTests.ExportDestinationClient
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class StorageExceptionParserTests
    {
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.NotFound)]
        [Theory]
        public void GivenValidErrorStatusCode_WhenParseStorageException_ThenCorrespondingHttpStatusCode(HttpStatusCode statusCode)
        {
            RequestResult requestResult = new RequestResult { HttpStatusCode = (int)statusCode };
            StorageException ex = new StorageException(requestResult, "exception message", new Exception("inner exception message"));

            var resultCode = StorageExceptionParser.ParseStorageException(ex);

            Assert.Equal(statusCode, resultCode);
        }

        [InlineData(0)]
        [InlineData(1)]
        [InlineData(777)]
        [Theory]
        public void GivenInValidStatusCode_WhenParseStorageException_ThenReturnsInternalServerError(int statusCode)
        {
            RequestResult requestResult = new RequestResult { HttpStatusCode = statusCode };
            StorageException ex = new StorageException(requestResult, "exception message", new Exception("inner exception message"));

            var resultCode = StorageExceptionParser.ParseStorageException(ex);

            Assert.Equal(HttpStatusCode.InternalServerError, resultCode);
        }

        [Fact]
        public void GivenNoRequestInformationAndUnknownHostMessage_WhenParseStorageException_ThenReturnsBadRequest()
        {
            StorageException ex = new StorageException("No such host is known");

            var resultCode = StorageExceptionParser.ParseStorageException(ex);

            Assert.Equal(HttpStatusCode.BadRequest, resultCode);
        }
    }
}
