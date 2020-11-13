// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters
{
    public class SearchParameterFilterAttributeTests
    {
        private readonly ISearchParameterValidator _searchParameterValidator = Substitute.For<ISearchParameterValidator>();
        private readonly ISearchParameterEditor _searchParameterEditor = Substitute.For<ISearchParameterEditor>();

        [Fact]
        public async Task GivenAnAction_WhenPostingAnObservationObject_ThenNoSearchParameterActionTaken()
        {
            var filter = new SearchParameterFilterAttribute(_searchParameterValidator, _searchParameterEditor);

            var context = CreateContext(new Observation());
            var actionExecutedContext = new ActionExecutedContext(context, new List<IFilterMetadata>(), null);
            ActionExecutionDelegate actionExecutionDelegate = () => Task.Run(() => actionExecutedContext);

            await filter.OnActionExecutionAsync(context, actionExecutionDelegate);

            await _searchParameterValidator.DidNotReceive().ValidateSearchParamterInput(Arg.Any<SearchParameter>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            await _searchParameterEditor.DidNotReceive().AddSearchParameterAsync(Arg.Any<SearchParameter>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAnAction_WhenPostingASearchParameterObject_ThenSearchParameterActionsTaken()
        {
            var filter = new SearchParameterFilterAttribute(_searchParameterValidator, _searchParameterEditor);

            var context = CreateContext(new SearchParameter());
            var actionExecutedContext = new ActionExecutedContext(context, new List<IFilterMetadata>(), null);
            ActionExecutionDelegate actionExecutionDelegate = () => Task.Run(() => actionExecutedContext);

            await filter.OnActionExecutionAsync(context, actionExecutionDelegate);

            await _searchParameterValidator.Received().ValidateSearchParamterInput(Arg.Any<SearchParameter>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            await _searchParameterEditor.Received().AddSearchParameterAsync(Arg.Any<SearchParameter>(), Arg.Any<CancellationToken>());
        }

        private static ActionExecutingContext CreateContext(Base type)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = "POST";
            return new ActionExecutingContext(
                new ActionContext(httpContext, new RouteData { Values = { [KnownActionParameterNames.ResourceType] = type.TypeName } }, new ActionDescriptor()),
                new List<IFilterMetadata>(),
                new Dictionary<string, object> { { "resource", type } },
                FilterTestsHelper.CreateMockFhirController());
        }
    }
}
