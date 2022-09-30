// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Security
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Security)]
    public class RoleLoaderTests
    {
        public static IEnumerable<object[]> GetInvalidRoles()
        {
            yield return new object[]
            {
                "empty name",
                new
                {
                    roles = new[]
                    {
                        new
                        {
                            name = string.Empty,
                            dataActions = new[] { "*" },
                            notDataActions = new[] { "hardDelete" },
                            scopes = new[] { "/" },
                        },
                    },
                },
            };

            yield return new object[]
            {
                "actions missing",
                new
                {
                    roles = new[]
                    {
                        new
                        {
                            name = "abc",
                            notDataActions = new[] { "hardDelete" },
                            scopes = new[] { "/" },
                        },
                    },
                },
            };

            yield return new object[]
            {
                "invalid notAction",
                new
                {
                    roles = new[]
                    {
                        new
                        {
                            name = "abc",
                            dataActions = new[] { "*" },
                            notDataActions = new[] { "abc" },
                            scopes = new[] { "/" },
                        },
                    },
                },
            };

            yield return new object[]
            {
                "missing scopes",
                new
                {
                    roles = new[]
                    {
                        new
                        {
                            name = "abc",
                            dataActions = new[] { "*" },
                            notDataActions = new[] { "hardDelete" },
                        },
                    },
                },
            };

            yield return new object[]
            {
                "scope not /",
                new
                {
                    roles = new[]
                    {
                        new
                        {
                            name = "abc",
                            dataActions = new[] { "*" },
                            notDataActions = new[] { "hardDelete" },
                            scopes = new[] { "/a" },
                        },
                    },
                },
            };

            yield return new object[]
            {
                "scope not single /",
                new
                {
                    roles = new[]
                    {
                        new
                        {
                            name = "abc",
                            dataActions = new[] { "*" },
                            notDataActions = new[] { "hardDelete" },
                            scopes = new[] { "/", "/" },
                        },
                    },
                },
            };

            yield return new object[]
            {
                "role name duplicated",
                new
                {
                    roles = new[]
                    {
                        new
                        {
                            name = "abc",
                            dataActions = new[] { "*" },
                            notDataActions = new[] { "hardDelete" },
                            scopes = new[] { "/" },
                        },
                        new
                        {
                            name = "abc",
                            dataActions = new[] { "*" },
                            notDataActions = new string[] { },
                            scopes = new[] { "/" },
                        },
                    },
                },
            };
        }

        [Fact]
        public async Task GivenValidRoles_WhenLoaded_AreProperlyTransformed()
        {
            var roles = new
            {
                roles = new[]
                {
                    new
                    {
                        name = "x",
                        dataActions = new[] { "*" },
                        notDataActions = new[] { "hardDelete" },
                        scopes = new[] { "/" },
                    },
                },
            };

            AuthorizationConfiguration authConfig = await LoadAsync(roles);

            Role actualRole = Assert.Single(authConfig.Roles);
            Assert.Equal(roles.roles.First().name, actualRole.Name);
            Assert.Equal(DataActions.All & ~DataActions.HardDelete, actualRole.AllowedDataActions);
        }

        [Fact]
        public async Task GivenValidDataActions_WhenSpecifiedAsRoleActions_AreRecognized()
        {
            var actionNames = Enum.GetValues(typeof(DataActions)).Cast<DataActions>()
                .Where(a => a != DataActions.All && a != DataActions.None);

            var roles = new
            {
                roles = actionNames.Select(a =>
                    new
                    {
                        name = $"role{a}",
                        dataActions = new[] { char.ToLower(a.ToString()[0]) + a.ToString().Substring(1) },
                        notDataActions = new string[] { },
                        scopes = new[] { "/" },
                    }).ToArray(),
            };

            AuthorizationConfiguration authConfig = await LoadAsync(roles);

            Assert.All(
                actionNames.Zip(authConfig.Roles.Select(r => r.AllowedDataActions)),
                t => Assert.Equal(t.First, t.Second));
        }

        [Theory]
        [MemberData(nameof(GetInvalidRoles))]
        public async Task GivenInvalidRoles_WhenLoaded_RaiseValidationErrors(string description, object roles)
        {
            Assert.NotEmpty(description);
            await Assert.ThrowsAsync<InvalidDefinitionException>(() => LoadAsync(roles));
        }

        private static async Task<AuthorizationConfiguration> LoadAsync(object roles)
        {
            IFileProvider fileProvider = Substitute.For<IFileProvider>();
            var hostEnvironment = Substitute.For<IHostEnvironment>();
            hostEnvironment.ContentRootFileProvider
                .GetFileInfo("roles.json")
                .CreateReadStream()
                .Returns(new MemoryStream(Encoding.UTF8.GetBytes(JObject.FromObject(roles).ToString())));

            var authConfig = new AuthorizationConfiguration();
            var roleLoader = new RoleLoader(authConfig, hostEnvironment);
            await roleLoader.StartAsync(CancellationToken.None);
            return authConfig;
        }
    }
}
