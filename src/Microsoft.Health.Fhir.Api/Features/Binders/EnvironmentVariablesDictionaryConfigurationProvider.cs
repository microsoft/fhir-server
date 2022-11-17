// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Api.Features.Binders;

/// <summary>
/// Detects JSON dictionaries in environment variables and converts them to <see cref="IConfiguration"/> values.
/// This allows the built-in binder to hydrate <see cref="Dictionary{TKey,TValue}"/> typed configuration properties.
/// </summary>
public class EnvironmentVariablesDictionaryConfigurationProvider : IConfigurationProvider
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

    /// <summary>
    /// The configuration key value pairs for this provider.
    /// </summary>
    protected IDictionary<string, string> Data { get; private set; }

    public IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string parentPath) => _configurationProvider.GetChildKeys(earlierKeys, parentPath);

    public IChangeToken GetReloadToken() => _configurationProvider.GetReloadToken();

    public void Load()
    {
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
            else
            {
                data.Add(environmentVariableName, environmentVariableValue);
            }
        }

        Data = data;
    }

    public void Set(string key, string value) => _configurationProvider.Set(key, value);

    public bool TryGet(string key, out string value) => Data.TryGetValue(key, out value);
}
