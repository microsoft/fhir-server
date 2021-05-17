// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Schema;
using Newtonsoft.Json.Serialization;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    /// <summary>
    /// Reads in roles from roles.json and validates against roles.json.schema and then
    /// sets <see cref="AuthorizationConfiguration.Roles"/>.
    /// We do not use asp.net configuration for reading in these settings
    /// because the binder provides no error handling (and its merging
    /// behavior when multiple config providers set array elements can
    /// lead to unexpected results)
    /// </summary>
    public class RoleLoader : IHostedService
    {
        private readonly AuthorizationConfiguration _authorizationConfiguration;
        private readonly IHostEnvironment _hostEnvironment;
        private readonly Microsoft.Extensions.FileProviders.IFileProvider _fileProvider;

        public RoleLoader(AuthorizationConfiguration authorizationConfiguration, IHostEnvironment hostEnvironment)
        {
            EnsureArg.IsNotNull(authorizationConfiguration, nameof(authorizationConfiguration));
            EnsureArg.IsNotNull(hostEnvironment, nameof(hostEnvironment));
            EnsureArg.IsNotNull(hostEnvironment.ContentRootFileProvider, nameof(hostEnvironment.ContentRootFileProvider));

            _authorizationConfiguration = authorizationConfiguration;
            _hostEnvironment = hostEnvironment;
            _fileProvider = hostEnvironment.ContentRootFileProvider;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            using Stream schemaContents = GetType().Assembly.GetManifestResourceStream(GetType(), "roles.schema.json");

            using Stream rolesContents = _fileProvider.GetFileInfo("roles.json").CreateReadStream();

            var jsonSerializer = JsonSerializer.Create(new JsonSerializerSettings { Converters = { new StringEnumConverter(new CamelCaseNamingStrategy()) } });

            using var schemaReader = new JsonTextReader(new StreamReader(schemaContents));
            using var jsonReader = new JsonTextReader(new StreamReader(rolesContents));
            using var validatingReader = new JSchemaValidatingReader(jsonReader)
            {
                Schema = JSchema.Load(schemaReader),
            };

            validatingReader.ValidationEventHandler += (sender, args) =>
                throw new InvalidDefinitionException(string.Format(Resources.ErrorValidatingRoles, args.Message));

            RolesContract roleContract = jsonSerializer.Deserialize<RolesContract>(validatingReader);

            _authorizationConfiguration.Roles = roleContract.Roles.Select(RoleContractToRole).ToArray();

            // validate that names are all unique
            foreach (IGrouping<string, Role> grouping in _authorizationConfiguration.Roles.GroupBy(r => r.Name))
            {
                if (grouping.Count() > 1)
                {
                    throw new InvalidDefinitionException(
                        string.Format(CultureInfo.CurrentCulture, Resources.DuplicateRoleNames, grouping.Count(), grouping.Key));
                }
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private Role RoleContractToRole(RoleContract roleContract)
        {
            DataActions dataActions = roleContract.DataActions.Aggregate(default(DataActions), (acc, a) => acc | a);
            DataActions notDataActions = roleContract.NotDataActions.Aggregate(default(DataActions), (acc, a) => acc | a);

            return new Role(roleContract.Name, dataActions & ~notDataActions, roleContract.Scopes.Single());
        }

        private class RolesContract
        {
            public RoleContract[] Roles { get; set; }
        }

        private class RoleContract
        {
            public string Name { get; set; }

            public DataActions[] DataActions { get; set; }

            public DataActions[] NotDataActions { get; set; }

            public string[] Scopes { get; set; }
        }
    }
}
