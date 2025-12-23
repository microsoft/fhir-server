// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Features.Exceptions;
using Microsoft.Health.Fhir.Api.Features.Resources;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Resources
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class ProvenanceHeaderBehaviorTests
    {
        private readonly FhirJsonParser _fhirJsonParser;
        private readonly ProvenanceHeaderBehavior _behavior;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IMediator _mediator;
        private readonly IProvenanceHeaderState _state;

        public ProvenanceHeaderBehaviorTests()
        {
            var resource = new Provenance()
            {
                Id = Guid.NewGuid().ToString(),
            };

            var wrapper = new ResourceWrapper(
                resource.ToResourceElement(),
                new RawResource(resource.ToJson(), FhirResourceFormat.Json, false),
                null,
                false,
                null,
                null,
                null);
            _mediator = Substitute.For<IMediator>();
            _mediator.Send<UpsertResourceResponse>(
                Arg.Any<CreateResourceRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new UpsertResourceResponse(new SaveOutcome(new RawResourceElement(wrapper), SaveOutcomeType.Created)));

            _fhirJsonParser = new FhirJsonParser();
            _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
            _httpContextAccessor.HttpContext.Returns(new DefaultHttpContext());

            _state = Substitute.For<IProvenanceHeaderState>();
            _state.Intercepted.Returns(true);

            _behavior = new ProvenanceHeaderBehavior(
                _fhirJsonParser,
                _httpContextAccessor,
                _mediator,
                _state);
        }

        [Theory]
        [InlineData(false, true, true, false, true)] // Provenance resource created
        [InlineData(true, true, true, false, true)] // Intercepted - Provenance resource not created
        [InlineData(false, false, true, false, true)] // 'X-Provenance' header missing - Provenance resource not created
        [InlineData(false, true, false, false, true)] // Invalid header value - ProvenanceHeaderException expected
        [InlineData(false, true, true, true, true)] // Provenance.Target.Count > 0 in the header - ProvenanceHeaderException expected
        [InlineData(false, true, true, false, false)] // Null response from the delegate - Provenance resource created
        public async Task GivenConditionalUpsertResourceRequest_WhenHandling_ThenProvenanceShouldBeCreated(
            bool intercepted,
            bool addHeader,
            bool addValidHeaderValue,
            bool addTarget,
            bool returnResponse)
        {
            await Run(
                intercepted,
                addHeader,
                addValidHeaderValue,
                addTarget,
                returnResponse,
                (next, token) => _behavior.Handle((ConditionalUpsertResourceRequest)null, next, token));
        }

        [Theory]
        [InlineData(false, true, true, false, true)] // Provenance resource created
        [InlineData(true, true, true, false, true)] // Intercepted - Provenance resource not created
        [InlineData(false, false, true, false, true)] // 'X-Provenance' header missing - Provenance resource not created
        [InlineData(false, true, false, false, true)] // Invalid header value - ProvenanceHeaderException expected
        [InlineData(false, true, true, true, true)] // Provenance.Target.Count > 0 in the header - ProvenanceHeaderException expected
        [InlineData(false, true, true, false, false)] // Null response from the delegate - Provenance resource created
        public async Task GivenConditionalCreateResourceRequest_WhenHandling_ThenProvenanceShouldBeCreated(
            bool intercepted,
            bool addHeader,
            bool addValidHeaderValue,
            bool addTarget,
            bool returnResponse)
        {
            await Run(
                intercepted,
                addHeader,
                addValidHeaderValue,
                addTarget,
                returnResponse,
                (next, token) => _behavior.Handle((ConditionalCreateResourceRequest)null, next, token));
        }

        [Theory]
        [InlineData(false, true, true, false, true)] // Provenance resource created
        [InlineData(true, true, true, false, true)] // Intercepted - Provenance resource not created
        [InlineData(false, false, true, false, true)] // 'X-Provenance' header missing - Provenance resource not created
        [InlineData(false, true, false, false, true)] // Invalid header value - ProvenanceHeaderException expected
        [InlineData(false, true, true, true, true)] // Provenance.Target.Count > 0 in the header - ProvenanceHeaderException expected
        [InlineData(false, true, true, false, false)] // Null response from the delegate - Provenance resource created
        public async Task GivenUpsertResourceRequest_WhenHandling_ThenProvenanceShouldBeCreated(
            bool intercepted,
            bool addHeader,
            bool addValidHeaderValue,
            bool addTarget,
            bool returnResponse)
        {
            await Run(
                intercepted,
                addHeader,
                addValidHeaderValue,
                addTarget,
                returnResponse,
                (next, token) => _behavior.Handle((UpsertResourceRequest)null, next, token));
        }

        [Theory]
        [InlineData(false, true, true, false, true)] // Provenance resource created
        [InlineData(true, true, true, false, true)] // Intercepted - Provenance resource not created
        [InlineData(false, false, true, false, true)] // 'X-Provenance' header missing - Provenance resource not created
        [InlineData(false, true, false, false, true)] // Invalid header value - ProvenanceHeaderException expected
        [InlineData(false, true, true, true, true)] // Provenance.Target.Count > 0 in the header - ProvenanceHeaderException expected
        [InlineData(false, true, true, false, false)] // Null response from the delegate - Provenance resource created
        public async Task GivenCreateResourceRequest_WhenHandling_ThenProvenanceShouldBeCreated(
            bool intercepted,
            bool addHeader,
            bool addValidHeaderValue,
            bool addTarget,
            bool returnResponse)
        {
            await Run(
                intercepted,
                addHeader,
                addValidHeaderValue,
                addTarget,
                returnResponse,
                (next, token) => _behavior.Handle((CreateResourceRequest)null, next, token));
        }

        private async Task Run(
            bool intercepted,
            bool addHeader,
            bool addValidHeaderValue,
            bool addTarget,
            bool returnResponse,
            Func<RequestHandlerDelegate<UpsertResourceResponse>, CancellationToken, Task<UpsertResourceResponse>> func)
        {
            _state.Intercepted.Returns(intercepted);
            if (addHeader)
            {
                var value = "invalid header value";
                if (addValidHeaderValue)
                {
                    var r = new Provenance();
                    if (addTarget)
                    {
                        r.Target.Add(new ResourceReference(Guid.NewGuid().ToString()));
                    }

                    value = r.ToJson();
                }

                _httpContextAccessor.HttpContext.Request.Headers[KnownHeaders.ProvenanceHeader] =
                    new StringValues(value);
            }

            var resource = new Patient()
            {
                Id = Guid.NewGuid().ToString(),
                VersionId = Guid.NewGuid().ToString(),
            };

            var reference = $"{resource.TypeName}/{resource.Id}/_history/{resource.VersionId}";
            RequestHandlerDelegate<UpsertResourceResponse> next = (token) =>
            {
                if (!returnResponse)
                {
                    return Task.FromResult<UpsertResourceResponse>(null);
                }

                var wrapper = new ResourceWrapper(
                    resource.ToResourceElement(),
                    new RawResource(resource.ToJson(), FhirResourceFormat.Json, false),
                    null,
                    false,
                    null,
                    null,
                    null);
                var outcome = new SaveOutcome(
                    new RawResourceElement(wrapper),
                    SaveOutcomeType.Created);
                return Task.FromResult(new UpsertResourceResponse(outcome));
            };

            var request = default(CreateResourceRequest);
            _mediator
                .When(x => x.Send<UpsertResourceResponse>(
                        Arg.Any<CreateResourceRequest>(),
                        Arg.Any<CancellationToken>()))
                .Do(x => request = x.Arg<CreateResourceRequest>());

            try
            {
                await func(
                    next,
                    CancellationToken.None);
                Assert.False(addHeader && (!addValidHeaderValue || addTarget));
            }
            catch (ProvenanceHeaderException)
            {
                Assert.True(addHeader && (!addValidHeaderValue || addTarget));
            }

            var shouldCreateProvenance = !intercepted
                && addHeader
                && addValidHeaderValue
                && !addTarget
                && returnResponse;
            if (shouldCreateProvenance)
            {
                Assert.NotNull(request?.Resource);

                var r = request.Resource.ToPoco() as Provenance;
                Assert.NotNull(r);
                Assert.Single(r.Target);
                Assert.Contains(
                    r.Target,
                    x =>
                    {
                        return string.Equals(reference, x.Reference, StringComparison.OrdinalIgnoreCase);
                    });
            }

            await _mediator.Received(shouldCreateProvenance ? 1 : 0).Send<UpsertResourceResponse>(
                Arg.Any<CreateResourceRequest>(),
                Arg.Any<CancellationToken>());
        }
    }
}
