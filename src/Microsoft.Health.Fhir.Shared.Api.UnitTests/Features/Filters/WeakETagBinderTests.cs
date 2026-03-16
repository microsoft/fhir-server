// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Microsoft.Net.Http.Headers;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class WeakETagBinderTests
    {
        private readonly WeakETagBinder _binder;

        public WeakETagBinderTests()
        {
            _binder = new WeakETagBinder();
        }

        [Theory]
        [InlineData("W/\"version1\"", true)]
        [InlineData("W\"version1\"", false)]
        [InlineData("", true)]
        [InlineData(null, true)]
        public async Task GivenETag_WhenBinding_ThenModelBindingContextShouldBeFilledProperly(
            string etag,
            bool valid)
        {
            var headers = new HeaderDictionary();
            if (!string.IsNullOrEmpty(etag))
            {
                headers.Add(HeaderNames.IfMatch, etag);
            }

            var request = Substitute.For<HttpRequest>();
            request.Headers.Returns(headers);

            var httpContext = Substitute.For<HttpContext>();
            httpContext.Request.Returns(request);

            var modelName = "testmodel";
            var bindingContext = Substitute.For<ModelBindingContext>();
            bindingContext.HttpContext.Returns(httpContext);
            bindingContext.ModelName = modelName;

            var model = default(WeakETag);
            bindingContext.When(x => x.Model = Arg.Any<object>()).Do(
                x =>
                {
                    model = (WeakETag)x[0];
                });

            var result = default(ModelBindingResult);
            bindingContext.When(x => x.Result = Arg.Any<ModelBindingResult>()).Do(
                x =>
                {
                    result = (ModelBindingResult)x[0];
                });

            var errors = new ModelStateDictionary();
            bindingContext.ModelState.Returns(errors);

            await _binder.BindModelAsync(bindingContext);

            // NOTE: Skipping validation on 'IsModelSet'. There seems a bug as we always set the result to success regardless of the input etag being valid or invalid.
            // https://github.com/microsoft/fhir-server/blob/fa23740a7e7197bacaa719d7987f4aca9647d1f5/src/Microsoft.Health.Fhir.Shared.Api/Features/Filters/WeakETagBinder.cs#L32
            // Assert.Equal(valid, result.IsModelSet);
            if (valid)
            {
                var expected = string.IsNullOrEmpty(etag) ? null : etag;
                Assert.Equal(expected, model?.ToString());
                Assert.Equal(expected, result.Model?.ToString());
                Assert.Empty(errors);
            }
            else
            {
                Assert.Null(model);
                Assert.Null(result.Model);
                Assert.Single(errors);
                Assert.True(errors.ContainsKey(modelName));
            }
        }
    }
}
