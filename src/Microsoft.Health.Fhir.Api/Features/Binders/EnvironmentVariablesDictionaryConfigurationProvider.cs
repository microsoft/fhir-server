// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Api.Features.Binders;

public class EnvironmentVariablesDictionaryConfigurationProvider : EnvironmentVariablesConfigurationProvider
{
    public override void Load()
    {
        base.Load();

        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, string> item in Data)
        {
            if (item.Value?.Trim().StartsWith("{", StringComparison.Ordinal) == true)
            {
                Dictionary<string, string> asDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(item.Value);

                foreach (KeyValuePair<string, string> kvp in asDictionary!)
                {
                    data.Add($"{item.Key}:{kvp.Key}", kvp.Value);
                }
            }
        }

        Data = data;
    }
}
