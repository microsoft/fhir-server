// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace Microsoft.Health.Fhir.Web
{
    public class DevelopmentAuthEnvironmentConfigurationSource : IConfigurationSource
    {
        private readonly string _filePath;

        public DevelopmentAuthEnvironmentConfigurationSource(string filePath)
        {
            _filePath = filePath;
        }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            var jsonConfigurationSource = new JsonConfigurationSource
            {
                Path = _filePath == null ? null : Path.GetFullPath(_filePath),
                Optional = true,
            };

            jsonConfigurationSource.ResolveFileProvider();
            return new Provider(jsonConfigurationSource);
        }

        private class Provider : JsonConfigurationProvider
        {
            private readonly Dictionary<string, string> _mappings = new Dictionary<string, string>
            {
                { "^roles:", "FhirServer:Security:Authorization:RoleConfiguration:Roles:" },
                { "^users:", "DevelopmentIdentityProvider:Users:" },
                { "^clientApplications:", "DevelopmentIdentityProvider:ClientApplications:" },
            };

            public Provider(JsonConfigurationSource source)
                : base(source)
            {
            }

            public override void Load()
            {
                base.Load();

                Data = Data.ToDictionary(
                    p => _mappings.Aggregate(p.Key, (acc, mapping) => Regex.Replace(acc, mapping.Key, mapping.Value, RegexOptions.IgnoreCase)),
                    p => p.Value,
                    StringComparer.OrdinalIgnoreCase);

                Data["FhirServer:Security:Authentication:Audience"] = DevelopmentIdentityProviderConfiguration.Audience;
                Data["DevelopmentIdentityProvider:Enabled"] = bool.TrueString;
            }
        }
    }
}
