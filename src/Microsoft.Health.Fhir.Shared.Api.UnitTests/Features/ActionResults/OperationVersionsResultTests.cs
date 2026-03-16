// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Core.Features.Operations.Versions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.ActionResults
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class OperationVersionsResultTests
    {
        private readonly OperationVersionsResult _result;
        private readonly VersionsResult _versionsResult;

        public OperationVersionsResultTests()
        {
            _versionsResult = new VersionsResult(
                new[] { "1.1", "1.2" },
                "1.0");
            _result = new OperationVersionsResult(
                _versionsResult,
                HttpStatusCode.Accepted);
        }

        [Theory]
        [InlineData("application/json")]
        [InlineData("application/xml")]
        [InlineData("application/fhir+json")]
        [InlineData("application/fhir+xml")]
        [InlineData("application/json", "application/fhir+json")]
        [InlineData("application/fhir+json", "application/json")]
        [InlineData("*/*")]
        [InlineData("application/json", "application/fhir+json", "*/*")]
        [InlineData(null)]
        public void GivenAcceptHeaders_WhenGettingResult_ThenCorrectResultShouldBeReturned(
            params string[] mediaTypes)
        {
            var acceptHeaders = default(List<MediaTypeHeaderValue>);
            if (mediaTypes != null)
            {
                acceptHeaders = new List<MediaTypeHeaderValue>();
                foreach (var mediaType in mediaTypes)
                {
                    acceptHeaders.Add(new MediaTypeHeaderValue(mediaType));
                }

                var fieldInfo = typeof(OperationVersionsResult).GetField(
                    "_acceptHeaders",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                fieldInfo.SetValue(_result, acceptHeaders);
            }

            var methodInfo = typeof(OperationVersionsResult).GetMethod(
                "GetResultToSerialize",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var result = methodInfo.Invoke(_result, new object[] { });
            Validate(result, acceptHeaders);
        }

        private void Validate(
            object result,
            List<MediaTypeHeaderValue> mediaTypes)
        {
            Assert.NotNull(result);

            var fhirMediaType = mediaTypes == null
                || !mediaTypes.Any()
                || mediaTypes.Any(x => string.Equals(x.ToString(), "*/*", StringComparison.OrdinalIgnoreCase))
                || mediaTypes.First().SubType.ToString().StartsWith("fhir", StringComparison.OrdinalIgnoreCase);
            if (fhirMediaType)
            {
                Assert.Equal(typeof(Parameters), result.GetType());

                var parameters = (Parameters)result;
                Assert.Contains(
                    parameters.Parameter,
                    x =>
                    {
                        return string.Equals(x.Name, "default", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(x.Value?.ToString(), _versionsResult.DefaultVersion, StringComparison.OrdinalIgnoreCase);
                    });
                Assert.Contains(
                    parameters.Parameter,
                    x =>
                    {
                        return string.Equals(x.Name, "version", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(x.Value?.ToString(), _versionsResult.Versions.First(), StringComparison.OrdinalIgnoreCase);
                    });
            }
            else
            {
                Assert.Equal(typeof(VersionsResult), result.GetType());

                var versionsResult = (VersionsResult)result;
                Assert.Equal(_versionsResult.DefaultVersion, versionsResult.DefaultVersion, StringComparer.OrdinalIgnoreCase);
                Assert.Equal(_versionsResult.Versions.Count, versionsResult.Versions?.Count);
                Assert.All(
                    _versionsResult.Versions,
                    x =>
                    {
                        Assert.Contains(
                            versionsResult.Versions,
                            y =>
                            {
                                return string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
                            });
                    });
            }
        }
    }
}
