// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Microsoft.Net.Http.Headers;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Headers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Export)]
    [Trait(Traits.Category, Categories.Web)]
    public class ExportResultExtensionsTests
    {
        [Fact]
        public void GivenAnExportResult_WhenSettingAContentLocationHeader_TheExportResultHasAContentLocationHeader()
        {
            string opName = OperationsConstants.Export;
            string id = Guid.NewGuid().ToString();
            var exportOperationUrl = new Uri($"http://localhost/{OperationsConstants.Operations}/{opName}/{id}");

            var urlResolver = Substitute.For<IUrlResolver>();
            urlResolver.ResolveOperationResultUrl(Arg.Any<string>(), Arg.Any<string>()).Returns(exportOperationUrl);

            var exportResult = JobResult<ExportJobResult>.Accepted().SetContentLocationHeader(urlResolver, opName, id);

            Assert.Equal(exportOperationUrl.AbsoluteUri, exportResult.Headers[HeaderNames.ContentLocation]);
        }

        [Fact]
        public void GivenAnExportResult_WhenSettingAContentTypeHeader_ThenExportResultHasAContentTypeHeader()
        {
            string contentTypeValue = "application/json";
            var exportResult = JobResult<ExportJobResult>.Accepted().SetContentTypeHeader(contentTypeValue);

            Assert.Equal(contentTypeValue, exportResult.Headers[HeaderNames.ContentType]);
        }
    }
}
