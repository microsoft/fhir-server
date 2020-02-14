// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Schema;
using Newtonsoft.Json.Serialization;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    public class RoleLoader : IStartable
    {
        private readonly AuthorizationConfiguration _authorizationConfiguration;
        private readonly IFileProvider _fileProvider;

        public RoleLoader(AuthorizationConfiguration authorizationConfiguration, IFileProvider fileProvider)
        {
            EnsureArg.IsNotNull(authorizationConfiguration, nameof(authorizationConfiguration));
            EnsureArg.IsNotNull(fileProvider, nameof(fileProvider));

            _authorizationConfiguration = authorizationConfiguration;
            _fileProvider = fileProvider;
        }

        public void Start()
        {
            using Stream schemaContent = _fileProvider.ReadFile("roles.schema.json");

            using Stream rolesContents = _fileProvider.ReadFile("roles.json");

            var jsonSerializer = JsonSerializer.Create(new JsonSerializerSettings { Converters = { new StringEnumConverter(new CamelCaseNamingStrategy()) } });

            var validatingReader = new JSchemaValidatingReader(new JsonTextReader(new StreamReader(rolesContents)))
            {
                Schema = JSchema.Load(new JsonTextReader(new StreamReader(schemaContent))),
            };

            validatingReader.ValidationEventHandler += (sender, args) => throw new InvalidDefinitionException(string.Format(Resources.ErrorValidatingRoles, args.Message));

            RolesContract roleContract = jsonSerializer.Deserialize<RolesContract>(validatingReader);

            _authorizationConfiguration.Roles = roleContract.Roles.Select(RoleContractToRole).ToArray();
        }

        private Role RoleContractToRole(RoleContract r)
        {
            ResourceActions actions = r.Actions.Aggregate(default(ResourceActions), (acc, a) => acc | a);
            ResourceActions notActions = r.NotActions.Aggregate(default(ResourceActions), (acc, a) => acc | a);

            return new Role(r.Name, actions & ~notActions);
        }

        private class RolesContract
        {
            public RoleContract[] Roles { get; set; }
        }

        private class RoleContract
        {
            public string Name { get; set; }

            public ResourceActions[] Actions { get; set; }

            public ResourceActions[] NotActions { get; set; }

            public string[] Scopes { get; set; }
        }
    }
}
