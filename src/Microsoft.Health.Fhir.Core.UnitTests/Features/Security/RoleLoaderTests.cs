// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Settings;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Security
{
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
                            actions = new[] { "*" },
                            notActions = new[] { "hardDelete" },
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
                            notActions = new[] { "hardDelete" },
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
                            actions = new[] { "*" },
                            notActions = new[] { "abc" },
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
                            actions = new[] { "*" },
                            notActions = new[] { "hardDelete" },
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
                            actions = new[] { "*" },
                            notActions = new[] { "hardDelete" },
                            scopes = new[] { "/a" },
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
                            actions = new[] { "*" },
                            notActions = new[] { "hardDelete" },
                            scopes = new[] { "/a" },
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
                            actions = new[] { "*" },
                            notActions = new[] { "hardDelete" },
                            scopes = new[] { "/" },
                        },
                        new
                        {
                            name = "abc",
                            actions = new[] { "*" },
                            notActions = new string[] { },
                            scopes = new[] { "/" },
                        },
                    },
                },
            };
        }

        [Fact]
        public void GivenValidRoles_WhenLoaded_AreProperlyTransformed()
        {
            var roles = new
            {
                roles = new[]
                {
                    new
                    {
                        name = "x",
                        actions = new[] { "*" },
                        notActions = new[] { "hardDelete" },
                        scopes = new[] { "/" },
                    },
                },
            };

            AuthorizationConfiguration authConfig = Load(roles);

            Role actualRole = Assert.Single(authConfig.Roles);
            Assert.Equal(roles.roles.First().name, actualRole.Name);
            Assert.Equal(FhirActions.All & ~FhirActions.HardDelete, actualRole.AllowedActions);
        }

        [Fact]
        public void GivenValidFhirActions_WhenSpecifiedAsRoleActions_AreRecognized()
        {
            var actionNames = Enum.GetValues(typeof(FhirActions)).Cast<FhirActions>()
                .Where(a => a != FhirActions.All && a != FhirActions.None);

            var roles = new
            {
                roles = actionNames.Select(a =>
                    new
                    {
                        name = $"role{a}",
                        actions = new[] { char.ToLower(a.ToString()[0]) + a.ToString().Substring(1) },
                        notActions = new string[] { },
                        scopes = new[] { "/" },
                    }).ToArray(),
            };

            AuthorizationConfiguration authConfig = Load(roles);

            Assert.All(
                actionNames.Zip(authConfig.Roles.Select(r => r.AllowedActions)),
                t => Assert.Equal(t.First, t.Second));
        }

        [Theory]
        [MemberData(nameof(GetInvalidRoles))]
        public void GivenInvalidRoles_WhenLoaded_RaiseValidationErrors(string description, object roles)
        {
            Assert.NotEmpty(description);
            Assert.Throws<InvalidDefinitionException>(() => Load(roles));
        }

        private static AuthorizationConfiguration Load(object roles)
        {
            IFileProvider fileProvider = Substitute.For<IFileProvider>();
            fileProvider.ReadFile(Arg.Any<string>()).Returns(new MemoryStream(Encoding.UTF8.GetBytes(JObject.FromObject(roles).ToString())));
            var authConfig = new AuthorizationConfiguration();
            var roleLoader = new RoleLoader(authConfig, fileProvider);
            roleLoader.Start();
            return authConfig;
        }
    }
}
