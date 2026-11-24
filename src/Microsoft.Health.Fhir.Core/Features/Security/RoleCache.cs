// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Caching;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Schema;
using Newtonsoft.Json.Serialization;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    /// <summary>
    /// The RBAC roles from roles.json, validated against roles.schema.json. Loaded once; roles never change at runtime.
    /// </summary>
    /// <remarks>
    /// We do not use ASP.NET configuration for these settings because the binder provides no error handling, and its
    /// merging behavior when multiple config providers set array elements can lead to unexpected results.
    /// </remarks>
    public sealed class RoleCache : GlobalCache<RoleSet>
    {
        private readonly IFileProvider _fileProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="RoleCache"/> class.
        /// </summary>
        /// <param name="hostEnvironment">The host environment whose content root contains roles.json.</param>
        /// <param name="logger">The logger.</param>
        public RoleCache(IHostEnvironment hostEnvironment, ILogger<RoleCache> logger)
            : base(logger)
        {
            EnsureArg.IsNotNull(hostEnvironment, nameof(hostEnvironment));
            _fileProvider = EnsureArg.IsNotNull(hostEnvironment.ContentRootFileProvider, nameof(hostEnvironment.ContentRootFileProvider));
        }

        /// <inheritdoc />
        /// <exception cref="InvalidDefinitionException">roles.json is invalid or contains duplicate role names.</exception>
        protected override ValueTask<RoleSet> GetRemoteDataAsync(bool async, CancellationToken cancellationToken) => new(LoadRoles());

        private RoleSet LoadRoles()
        {
            using Stream schemaContents = typeof(RoleCache).Assembly.GetManifestResourceStream(typeof(RoleCache), "roles.schema.json");
            using Stream rolesContents = _fileProvider.GetFileInfo("roles.json").CreateReadStream();

            var jsonSerializer = JsonSerializer.Create(new JsonSerializerSettings { Converters = { new StringEnumConverter(new CamelCaseNamingStrategy()) } });

            using var schemaReader = new JsonTextReader(new StreamReader(schemaContents));
            using var jsonReader = new JsonTextReader(new StreamReader(rolesContents));
            using var validatingReader = new JSchemaValidatingReader(jsonReader)
            {
                Schema = JSchema.Load(schemaReader),
            };

            validatingReader.ValidationEventHandler += (sender, args) =>
                throw new InvalidDefinitionException(string.Format(Core.Resources.ErrorValidatingRoles, args.Message));

            RolesContract roleContract = jsonSerializer.Deserialize<RolesContract>(validatingReader);
            Role[] roles = roleContract.Roles.Select(RoleContractToRole).ToArray();

            // validate that names are all unique
            foreach (IGrouping<string, Role> grouping in roles.GroupBy(r => r.Name))
            {
                if (grouping.Count() > 1)
                {
                    throw new InvalidDefinitionException(
                        string.Format(CultureInfo.CurrentCulture, Core.Resources.DuplicateRoleNames, grouping.Count(), grouping.Key));
                }
            }

            return new RoleSet(roles);
        }

        private static Role RoleContractToRole(RoleContract roleContract)
        {
            DataActions dataActions = roleContract.DataActions.Aggregate(default(DataActions), (acc, a) => acc | a);
            DataActions notDataActions = roleContract.NotDataActions.Aggregate(default(DataActions), (acc, a) => acc | a);

            return new Role(roleContract.Name, dataActions & ~notDataActions, roleContract.Scopes.Single());
        }

        private sealed class RolesContract
        {
            public RoleContract[] Roles { get; set; }
        }

        private sealed class RoleContract
        {
            public string Name { get; set; }

            public DataActions[] DataActions { get; set; }

            public DataActions[] NotDataActions { get; set; }

            public string[] Scopes { get; set; }
        }
    }
}
