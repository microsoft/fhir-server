// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Api.Features.Binders;

/// <summary>
/// Detects JSON dictionaries in variables and converts them to <see cref="IConfiguration"/> values.
/// This allows the built-in binder to hydrate <see cref="Dictionary{TKey,TValue}"/> typed configuration properties.
/// </summary>
/// <remarks>
/// This configuration provider ignores other variables if not in JSON format.
/// </remarks>
public class EnvironmentVariablesDictionaryConfigurationProvider : ConfigurationProvider
{
    private readonly IConfigurationProvider _configurationProvider;

    public EnvironmentVariablesDictionaryConfigurationProvider()
        : this(new EnvironmentVariablesConfigurationProvider())
    {
    }

    /// <summary>
    /// Creates a <see cref="EnvironmentVariablesDictionaryConfigurationProvider"/> with a customer <see cref="IConfigurationProvider"/>.
    /// </summary>
    /// <param name="configurationProvider">Custom configuration provider.</param>
    /// <remarks>
    /// Constructor only used for testing purposes.
    /// </remarks>
    public EnvironmentVariablesDictionaryConfigurationProvider(IConfigurationProvider configurationProvider)
    {
        EnsureArg.IsNotNull(configurationProvider, nameof(configurationProvider));

        _configurationProvider = configurationProvider;
        Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public override void Load()
    {
        base.Load();

        _configurationProvider.Load();

        IEnumerable<string> keys = _configurationProvider.GetChildKeys(Array.Empty<string>(), null);

        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string environmentVariableName in keys)
        {
            _configurationProvider.TryGet(environmentVariableName, out string environmentVariableValue);

            if (!string.IsNullOrEmpty(environmentVariableValue) && environmentVariableValue.Trim().StartsWith("{", StringComparison.Ordinal) == true)
            {
                Dictionary<string, string> asDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(environmentVariableValue);

                foreach (KeyValuePair<string, string> kvp in asDictionary!)
                {
                    data.Add($"{environmentVariableName}:{kvp.Key}", kvp.Value);
                }
            }
        }

        Data = data;
    }
}
