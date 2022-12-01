// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Api.Features.Binders;

/// <summary>
/// Detects JSON dictionaries in variables and converts them to <see cref="IConfiguration"/> values.
/// This allows the built-in binder to hydrate <see cref="Dictionary{TKey,TValue}"/> typed configuration properties.
/// </summary>
/// <remarks>
/// This configuration provider ignores other variables if not in JSON format.
/// </remarks>
public class DictionaryExpansionConfigurationProvider : ConfigurationProvider
{
    private readonly IConfigurationProvider _configurationProvider;

    /// <summary>
    /// Creates a <see cref="DictionaryExpansionConfigurationProvider"/> with a customer <see cref="IConfigurationProvider"/>.
    /// </summary>
    /// <param name="configurationProvider">Custom configuration provider.</param>
    /// <remarks>
    /// Constructor only used for testing purposes.
    /// </remarks>
    public DictionaryExpansionConfigurationProvider(IConfigurationProvider configurationProvider)
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

        EnumerateKeys(keys, data);

        Data = data;
    }

    private void EnumerateKeys(IEnumerable<string> keys, Dictionary<string, string> data, string path = null)
    {
        foreach (string keyName in keys.Distinct())
        {
            var keyPath = path != null ? $"{path}:{keyName}" : keyName;

            _configurationProvider.TryGet(keyPath, out string environmentVariableValue);

            if (!string.IsNullOrEmpty(environmentVariableValue)
                && TryParseDictionaryJson(environmentVariableValue, out Dictionary<string, string> asDictionary))
            {
                foreach (KeyValuePair<string, string> kvp in asDictionary!)
                {
                    data.Add($"{keyPath}:{kvp.Key}", kvp.Value);
                }
            }

            IEnumerable<string> innerKeys = _configurationProvider.GetChildKeys(Array.Empty<string>(), keyPath);
            EnumerateKeys(innerKeys, data, keyPath);
        }
    }

    private static bool TryParseDictionaryJson(string value, out Dictionary<string, string> dictionary)
    {
        dictionary = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.Trim();
        if (!value.Trim().StartsWith("{", StringComparison.Ordinal) || !value.Trim().EndsWith("}", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            dictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(value);
            return true;
        }
        catch (JsonReaderException)
        {
            return false;
        }
    }
}
