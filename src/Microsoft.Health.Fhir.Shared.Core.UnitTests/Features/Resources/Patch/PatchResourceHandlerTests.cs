// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Patch;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Resources.Patch;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Patch)]
public class PatchResourceHandlerTests
{
    private readonly PatchResourceHandler _patchHandler;
    private readonly IMediator _mediator;

    public PatchResourceHandlerTests()
    {
        IAuthorizationService<DataActions> authService = Substitute.For<IAuthorizationService<DataActions>>();
        IFhirDataStore fhirDataStore = Substitute.For<IFhirDataStore>();
        _mediator = Substitute.For<IMediator>();

        _patchHandler = Mock.TypeWithArguments<PatchResourceHandler>(_mediator, authService, fhirDataStore);

        authService
            .CheckAccess(Arg.Any<DataActions>(), CancellationToken.None)
            .Returns(x => ValueTask.FromResult((DataActions)x[0]));

        ResourceElement patient = Samples.GetDefaultPatient().UpdateVersion("1");

        var wrapper = new ResourceWrapper(
            patient,
            new RawResource(patient.Instance.ToJson(), FhirResourceFormat.Json, false),
            new ResourceRequest(HttpMethod.Get),
            false,
            null,
            null,
            null);

        fhirDataStore.GetAsync(Arg.Any<ResourceKey>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(wrapper));
    }

    [Fact]
    public async Task GivenAPatchResourceHandler_WhenHandlingAPatchResourceRequestWithETag_ThenTheRequestIsForwardedToTheMediator()
    {
        var etag = WeakETag.FromWeakETag("W/\"1\"");

        var request = new PatchResourceRequest(new ResourceKey("Patient", "123"), new FhirPathPatchPayload(new Parameters()), bundleResourceContext: null, weakETag: etag);
        await _patchHandler.Handle(request, CancellationToken.None);

        await _mediator
            .Received()
            .Send<UpsertResourceResponse>(Arg.Is<UpsertResourceRequest>(x => x.WeakETag.VersionId == etag.VersionId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenAPatchResourceHandler_WhenHandlingAPatchResourceRequestWithMismatchingETag_ThenTheAnExceptionIsThrown()
    {
        var etag = WeakETag.FromWeakETag("W/\"2\"");

        var request = new PatchResourceRequest(new ResourceKey("Patient", "123"), new FhirPathPatchPayload(new Parameters()), bundleResourceContext: null, weakETag: etag);

        await Assert.ThrowsAsync<PreconditionFailedException>(async () => await _patchHandler.Handle(request, CancellationToken.None));
    }
}
