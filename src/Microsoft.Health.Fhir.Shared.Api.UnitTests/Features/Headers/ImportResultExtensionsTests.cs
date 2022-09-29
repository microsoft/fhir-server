// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Microsoft.Net.Http.Headers;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Headers
{
    [Trait("Traits.OwningTeam", OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    [Trait(Traits.Category, Categories.Web)]
    public class ImportResultExtensionsTests
    {
        [Fact]
        public void GivenAnImportResult_WhenSettingAContentLocationHeader_ThenImportResultHasAContentLocationHeader()
        {
            string opName = OperationsConstants.Import;
            string id = Guid.NewGuid().ToString();
            var bulkImportOperationUrl = new Uri($"http://localhost/{OperationsConstants.Operations}/{opName}/{id}");

            var urlResolver = Substitute.For<IUrlResolver>();
            urlResolver.ResolveOperationResultUrl(Arg.Any<string>(), Arg.Any<string>()).Returns(bulkImportOperationUrl);

            var bulkImportResult = ImportResult.Accepted().SetContentLocationHeader(urlResolver, opName, id);

            Assert.Equal(bulkImportOperationUrl.AbsoluteUri, bulkImportResult.Headers[HeaderNames.ContentLocation]);
        }

        [Fact]
        public void GivenAnImportResult_WhenSettingAContentTypeHeader_ThenImportResultHasAContentTypeHeader()
        {
            string contentTypeValue = "application/json";
            var bulkImportResult = ImportResult.Accepted().SetContentTypeHeader(contentTypeValue);

            Assert.Equal(contentTypeValue, bulkImportResult.Headers[HeaderNames.ContentType]);
        }
    }
}
