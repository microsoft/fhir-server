// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Api.Features.Resources;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Delete;
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
    [Trait(Traits.Category, Categories.Profiles)]
    public sealed class ProfileResourcesBehaviourUnitTests
    {
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly IProvideProfilesForValidation _profilesResolver;
        private readonly ProfileResourcesBehaviour _profileResourcesBehaviour;

        public ProfileResourcesBehaviourUnitTests()
        {
            _authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.EditProfileDefinitions);

            _profilesResolver = Substitute.For<IProvideProfilesForValidation>();
            _profilesResolver.GetProfilesTypes().Returns(new HashSet<string>() { "ValueSet", "StructureDefinition", "CodeSystem" });

            _profileResourcesBehaviour = new ProfileResourcesBehaviour(_authorizationService, _profilesResolver);
        }

        [Fact]
        public async Task GivenProfileResourcesBehaviour_WhenExecutedOutOfBundleContext_ThenCallProfileResolverRefresh()
        {
            ValueSet valueSet = new ValueSet();
            ResourceElement resourceElement = new ResourceElement(valueSet.ToTypedElement());

            var requestHandlerDelegate = Substitute.For<RequestHandlerDelegate<UpsertResourceResponse>>();

            await _profileResourcesBehaviour.Handle(
                new CreateResourceRequest(
                    resourceElement,
                    bundleResourceContext: null),
                requestHandlerDelegate,
                default);

            // Out of the bundle context, ProfileResourcesBehaviour should call the profile resolver refresh.
            _profilesResolver.Received(1).Refresh();
        }

        [Fact]
        public async Task GivenProfileResourcesBehaviour_WhenExecutedUnderTheBundleContext_ThenDoNotCallProfileResolverRefresh()
        {
            var bundleResourceContext = new BundleResourceContext(
                bundleType: Hl7.Fhir.Model.Bundle.BundleType.Batch,
                processingLogic: BundleProcessingLogic.Parallel,
                httpVerb: Hl7.Fhir.Model.Bundle.HTTPVerb.POST,
                persistedId: null,
                bundleOperationId: Guid.NewGuid());

            ValueSet valueSet = new ValueSet();
            ResourceElement resourceElement = new ResourceElement(valueSet.ToTypedElement());

            var requestHandlerDelegate = Substitute.For<RequestHandlerDelegate<UpsertResourceResponse>>();

            await _profileResourcesBehaviour.Handle(
                new CreateResourceRequest(
                    resourceElement,
                    bundleResourceContext: bundleResourceContext),
                requestHandlerDelegate,
                default);

            // Under the bundle context, ProfileResourcesBehaviour should not call the profile resolver refresh.
            _profilesResolver.Received(0).Refresh();
        }

        [Fact]
        public async Task GivenConditionalUpsertResourceRequest_WhenHandling_ThenRequestShouldBeHandledSuccessfully()
        {
            await Run<UpsertResourceResponse>(
                x =>
                {
                    return _profileResourcesBehaviour.Handle(
                        new ConditionalUpsertResourceRequest(
                            new ResourceElement(new ValueSet().ToTypedElement()),
                            new List<Tuple<string, string>>()),
                        x,
                        default);
                });
        }

        [Fact]
        public async Task GivenConditionalCreateResourceRequest_WhenHandling_ThenRequestShouldBeHandledSuccessfully()
        {
            await Run<UpsertResourceResponse>(
                x =>
                {
                    return _profileResourcesBehaviour.Handle(
                        new ConditionalCreateResourceRequest(
                            new ResourceElement(new ValueSet().ToTypedElement()),
                            new List<Tuple<string, string>>()),
                        x,
                        default);
                });
        }

        [Fact]
        public async Task GivenUpsertResourceRequest_WhenHandling_ThenRequestShouldBeHandledSuccessfully()
        {
            await Run<UpsertResourceResponse>(
                x =>
                {
                    return _profileResourcesBehaviour.Handle(
                        new UpsertResourceRequest(new ResourceElement(new ValueSet().ToTypedElement())),
                        x,
                        default);
                });
        }

        [Fact]
        public async Task GivenDeleteResourceRequest_WhenHandling_ThenRequestShouldBeHandledSuccessfully()
        {
            await Run<DeleteResourceResponse>(
                x =>
                {
                    return _profileResourcesBehaviour.Handle(
                        new DeleteResourceRequest(
                            new ResourceKey(KnownResourceTypes.ValueSet, Guid.NewGuid().ToString()),
                            DeleteOperation.SoftDelete),
                        x,
                        default);
                });
        }

        [Fact]
        public async Task GivenRequest_WhenProfilerTypeIsUnknown_ThenRequestShouldBeBypassed()
        {
            ResourceElement resourceElement = new ResourceElement(new Patient().ToTypedElement());

            var requestHandlerDelegate = Substitute.For<RequestHandlerDelegate<UpsertResourceResponse>>();

            await _profileResourcesBehaviour.Handle(
                new CreateResourceRequest(
                    resourceElement,
                    bundleResourceContext: null),
                requestHandlerDelegate,
                default);

            _profilesResolver.DidNotReceive().Refresh();
            await requestHandlerDelegate.Received(1).Invoke();
        }

        private async Task Run<TResponse>(Func<RequestHandlerDelegate<TResponse>, Task<TResponse>> func)
        {
            var requestHandlerDelegate = Substitute.For<RequestHandlerDelegate<TResponse>>();

            await func(requestHandlerDelegate);

            // Out of the bundle context, ProfileResourcesBehaviour should call the profile resolver refresh.
            _profilesResolver.Received(1).Refresh();
        }
    }
}
