// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model.CdsHooks;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Security.Authorization
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Security)]
    public class AuthorizationServiceExtensionsTests
    {
        [Theory]
        [InlineData(true, true, DataActions.Write | DataActions.Create, DataActions.Write | DataActions.Create)]
        [InlineData(false, true, DataActions.Write, DataActions.Write)]
        [InlineData(true, true, DataActions.Write | DataActions.Create, DataActions.Write)]
        [InlineData(true, true, DataActions.Write | DataActions.Create, DataActions.Create)]
        [InlineData(true, true, DataActions.Write | DataActions.Create, DataActions.None)]
        [InlineData(true, false, DataActions.Write | DataActions.Create, DataActions.None)]
        public async Task GivenDataActions_WhenCheckingCreateAccess_ThenCheckAccessIsPerformedCorrectly(
            bool includeGranular,
            bool throwException,
            DataActions requested,
            DataActions granted)
        {
            await TestCheckAccess(
                requested,
                granted,
                throwException && granted == DataActions.None,
                service =>
                {
                    return service.CheckCreateAccess(
                        CancellationToken.None,
                        includeGranular,
                        throwException);
                });
        }

        [Theory]
        [InlineData(false, false, DataActions.Delete, DataActions.Delete)]
        [InlineData(true, false, DataActions.Delete | DataActions.HardDelete, DataActions.Delete | DataActions.HardDelete)]
        [InlineData(true, false, DataActions.Delete | DataActions.HardDelete, DataActions.Delete)]
        [InlineData(false, true, DataActions.Delete, DataActions.None)]
        [InlineData(false, false, DataActions.Delete, DataActions.None)]
        public async Task GivenDataActions_WhenCheckingDeleteAccess_ThenCheckAccessIsPerformedCorrectly(
            bool hardDelete,
            bool throwException,
            DataActions requested,
            DataActions granted)
        {
            await TestCheckAccess(
                requested,
                granted,
                throwException && granted != requested,
                service =>
                {
                    return service.CheckDeleteAccess(
                        CancellationToken.None,
                        hardDelete,
                        throwException);
                });
        }

        [Theory]
        [InlineData(true, true, DataActions.Read | DataActions.ReadById, DataActions.Read | DataActions.ReadById)]
        [InlineData(false, true, DataActions.Read, DataActions.Read)]
        [InlineData(true, true, DataActions.Read | DataActions.ReadById, DataActions.Read)]
        [InlineData(true, true, DataActions.Read | DataActions.ReadById, DataActions.ReadById)]
        [InlineData(true, true, DataActions.Read | DataActions.ReadById, DataActions.None)]
        [InlineData(true, false, DataActions.Read | DataActions.ReadById, DataActions.None)]
        public async Task GivenDataActions_WhenCheckingGetAccess_ThenCheckAccessIsPerformedCorrectly(
            bool includeGranular,
            bool throwException,
            DataActions requested,
            DataActions granted)
        {
            await TestCheckAccess(
                requested,
                granted,
                throwException && granted == DataActions.None,
                service =>
                {
                    return service.CheckGetAccess(
                        CancellationToken.None,
                        includeGranular,
                        throwException);
                });
        }

        [Theory]
        [InlineData(true, true, DataActions.Read | DataActions.Write | DataActions.Update, DataActions.Read | DataActions.Write | DataActions.Update)]
        [InlineData(false, true, DataActions.Read | DataActions.Write, DataActions.Read | DataActions.Write)]
        [InlineData(true, true, DataActions.Read | DataActions.Write | DataActions.Update, DataActions.Read | DataActions.Write)]
        [InlineData(true, true, DataActions.Read | DataActions.Write | DataActions.Update, DataActions.Update)]
        [InlineData(true, true, DataActions.Read | DataActions.Write | DataActions.Update, DataActions.None)]
        [InlineData(true, false, DataActions.Read | DataActions.Write | DataActions.Update, DataActions.None)]
        public async Task GivenDataActions_WhenCheckingPatchAccess_ThenCheckAccessIsPerformedCorrectly(
            bool includeGranular,
            bool throwException,
            DataActions requested,
            DataActions granted)
        {
            await TestCheckAccess(
                requested,
                granted,
                throwException && granted == DataActions.None,
                service =>
                {
                    return service.CheckPatchAccess(
                        CancellationToken.None,
                        includeGranular,
                        throwException);
                });
        }

        [Theory]
        [InlineData(true, true, DataActions.Read | DataActions.Search, DataActions.Read | DataActions.Search)]
        [InlineData(false, true, DataActions.Read, DataActions.Read)]
        [InlineData(true, true, DataActions.Read | DataActions.Search, DataActions.Read)]
        [InlineData(true, true, DataActions.Read | DataActions.Search, DataActions.Search)]
        [InlineData(true, true, DataActions.Read | DataActions.Search, DataActions.None)]
        [InlineData(true, false, DataActions.Read | DataActions.Search, DataActions.None)]
        public async Task GivenDataActions_WhenCheckingSearchAccess_ThenCheckAccessIsPerformedCorrectly(
            bool includeGranular,
            bool throwException,
            DataActions requested,
            DataActions granted)
        {
            await TestCheckAccess(
                requested,
                granted,
                throwException && granted == DataActions.None,
                service =>
                {
                    return service.CheckSearchAccess(
                        CancellationToken.None,
                        includeGranular,
                        throwException);
                });
        }

        [Theory]
        [InlineData(true, true, DataActions.Write | DataActions.Update, DataActions.Write | DataActions.Update)]
        [InlineData(false, true, DataActions.Write, DataActions.Write)]
        [InlineData(true, true, DataActions.Write | DataActions.Update, DataActions.Write)]
        [InlineData(true, true, DataActions.Write | DataActions.Update, DataActions.Update)]
        [InlineData(true, true, DataActions.Write | DataActions.Update, DataActions.None)]
        [InlineData(true, false, DataActions.Write | DataActions.Update, DataActions.None)]
        public async Task GivenDataActions_WhenCheckingUpdateAccess_ThenCheckAccessIsPerformedCorrectly(
            bool includeGranular,
            bool throwException,
            DataActions requested,
            DataActions granted)
        {
            await TestCheckAccess(
                requested,
                granted,
                throwException && granted == DataActions.None,
                service =>
                {
                    return service.CheckUpdateAccess(
                        CancellationToken.None,
                        includeGranular,
                        throwException);
                });
        }

        [Theory]
        [InlineData(true, true, DataActions.Write | DataActions.Update | DataActions.Create, DataActions.Write | DataActions.Update | DataActions.Create)]
        [InlineData(false, true, DataActions.Write, DataActions.Write)]
        [InlineData(true, true, DataActions.Write | DataActions.Update | DataActions.Create, DataActions.Write)]
        [InlineData(true, true, DataActions.Write | DataActions.Update | DataActions.Create, DataActions.Update | DataActions.Create)]
        [InlineData(true, true, DataActions.Write | DataActions.Update | DataActions.Create, DataActions.None)]
        [InlineData(true, false, DataActions.Write | DataActions.Update | DataActions.Create, DataActions.None)]
        public async Task GivenDataActions_WhenCheckingUpsertAccess_ThenCheckAccessIsPerformedCorrectly(
            bool includeGranular,
            bool throwException,
            DataActions requested,
            DataActions granted)
        {
            await TestCheckAccess(
                requested,
                granted,
                throwException && granted == DataActions.None,
                service =>
                {
                    return service.CheckUpsertAccess(
                        CancellationToken.None,
                        includeGranular,
                        throwException);
                });
        }

        [Theory]
        [InlineData(true, true, DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Create, DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Create)]
        [InlineData(false, true, DataActions.Read | DataActions.Write, DataActions.Read | DataActions.Write)]
        [InlineData(true, true, DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Create, DataActions.Read | DataActions.Write)]
        [InlineData(true, true, DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Create, DataActions.Search | DataActions.Create)]
        [InlineData(true, true, DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Create, DataActions.None)]
        [InlineData(true, false, DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Create, DataActions.None)]
        public async Task GivenDataActions_WhenCheckingConditionalCreateAccess_ThenCheckAccessIsPerformedCorrectly(
            bool includeGranular,
            bool throwException,
            DataActions requested,
            DataActions granted)
        {
            await TestCheckAccess(
                requested,
                granted,
                throwException && granted == DataActions.None,
                service =>
                {
                    return service.CheckConditionalCreateAccess(
                        CancellationToken.None,
                        includeGranular,
                        throwException);
                });
        }

        [Theory]
        [InlineData(false, true, true, DataActions.Read | DataActions.Delete | DataActions.Search, DataActions.Read | DataActions.Delete | DataActions.Search)]
        [InlineData(true, true, true, DataActions.Read | DataActions.Delete | DataActions.Search | DataActions.HardDelete, DataActions.Read | DataActions.Delete | DataActions.Search | DataActions.HardDelete)]
        [InlineData(false, false, true, DataActions.Read | DataActions.Delete, DataActions.Read | DataActions.Delete)]
        [InlineData(true, false, true, DataActions.Read | DataActions.Delete | DataActions.HardDelete, DataActions.Read | DataActions.Delete | DataActions.HardDelete)]
        [InlineData(false, true, true, DataActions.Read | DataActions.Delete | DataActions.Search, DataActions.Read | DataActions.Delete)]
        [InlineData(true, true, true, DataActions.Read | DataActions.Delete | DataActions.Search | DataActions.HardDelete, DataActions.Search | DataActions.Delete | DataActions.HardDelete)]
        [InlineData(false, true, true, DataActions.Read | DataActions.Delete | DataActions.Search, DataActions.None)]
        [InlineData(false, true, false, DataActions.Read | DataActions.Delete | DataActions.Search, DataActions.None)]
        public async Task GivenDataActions_WhenCheckingConditionalDeleteAccess_ThenCheckAccessIsPerformedCorrectly(
            bool hardDelete,
            bool includeGranular,
            bool throwException,
            DataActions requested,
            DataActions granted)
        {
            await TestCheckAccess(
                requested,
                granted,
                throwException && granted == DataActions.None,
                service =>
                {
                    return service.CheckConditionalDeleteAccess(
                        CancellationToken.None,
                        hardDelete,
                        includeGranular,
                        throwException);
                });
        }

        [Theory]
        [InlineData(true, true, DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update, DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update)]
        [InlineData(false, true, DataActions.Read | DataActions.Write, DataActions.Read | DataActions.Write)]
        [InlineData(true, true, DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update, DataActions.Read | DataActions.Write)]
        [InlineData(true, true, DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update, DataActions.Search | DataActions.Update)]
        [InlineData(true, true, DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update, DataActions.None)]
        [InlineData(true, false, DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update, DataActions.None)]
        public async Task GivenDataActions_WhenCheckingConditionalPatchAccess_ThenCheckAccessIsPerformedCorrectly(
            bool includeGranular,
            bool throwException,
            DataActions requested,
            DataActions granted)
        {
            await TestCheckAccess(
                requested,
                granted,
                throwException && granted == DataActions.None,
                service =>
                {
                    return service.CheckConditionalPatchAccess(
                        CancellationToken.None,
                        includeGranular,
                        throwException);
                });
        }

        [Theory]
        [InlineData(true, true, DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update, DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update)]
        [InlineData(false, true, DataActions.Read | DataActions.Write, DataActions.Read | DataActions.Write)]
        [InlineData(true, true, DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update, DataActions.Read | DataActions.Write)]
        [InlineData(true, true, DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update, DataActions.Search | DataActions.Update)]
        [InlineData(true, true, DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update, DataActions.None)]
        [InlineData(true, false, DataActions.Read | DataActions.Write | DataActions.Search | DataActions.Update, DataActions.None)]
        public async Task GivenDataActions_WhenCheckingConditionalUpdateAccess_ThenCheckAccessIsPerformedCorrectly(
            bool includeGranular,
            bool throwException,
            DataActions requested,
            DataActions granted)
        {
            await TestCheckAccess(
                requested,
                granted,
                throwException && granted == DataActions.None,
                service =>
                {
                    return service.CheckConditionalUpdateAccess(
                        CancellationToken.None,
                        includeGranular,
                        throwException);
                });
        }

        private static async Task TestCheckAccess(
            DataActions requested,
            DataActions granted,
            bool exception,
            Func<IAuthorizationService<DataActions>, Task<DataActions>> func)
        {
            var service = CreateAuthorizationService(requested, granted);
            try
            {
                var result = await func(service);
                Assert.True(!exception);
            }
            catch (UnauthorizedFhirActionException)
            {
                Assert.True(exception);
            }

            await service.Received(1).CheckAccess(
                Arg.Is<DataActions>(x => x == requested),
                Arg.Any<CancellationToken>());
        }

        private static IAuthorizationService<DataActions> CreateAuthorizationService(
            DataActions requested,
            DataActions granted)
        {
            var service = Substitute.For<IAuthorizationService<DataActions>>();
            service.CheckAccess(
                Arg.Is<DataActions>(x => x == requested),
                Arg.Any<CancellationToken>())
                .Returns(granted);
            return service;
        }
    }
}
