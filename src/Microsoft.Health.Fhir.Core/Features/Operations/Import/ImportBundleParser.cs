// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import;

public sealed class ImportBundleParser : IDisposable
{
    private readonly StreamReader streamReader;
    private readonly JsonTextReader jsonReader;

    public ImportBundleParser(Stream jsonData)
    {
        streamReader = new StreamReader(jsonData);
        jsonReader = new JsonTextReader(streamReader) { SupportMultipleContent = false };
    }

    public string BundleType { get; private set; }

    public HashSet<(string Key, string Value)> UnusedProperties { get; } = new();

    public async Task<string> ReadBundleType()
    {
        while (await jsonReader.ReadAsync())
        {
            switch (jsonReader.TokenType)
            {
                case JsonToken.PropertyName:
                    var propertyName = jsonReader.Value.ToString();
                    await jsonReader.ReadAsync(); // Move to the property value.

                    if (propertyName == "type" && jsonReader.TokenType == JsonToken.String)
                    {
                        BundleType = jsonReader.Value.ToString();
                        return BundleType;
                    }

                    if (propertyName == "entry")
                    {
                        throw new NotSupportedException("bundle type was not found before entry was encountered.");
                    }

                    break;
            }
        }

        throw new NotSupportedException("No bundle type found.");
    }

    public async IAsyncEnumerable<string> ReadEntries()
    {
        while (await jsonReader.ReadAsync())
        {
            switch (jsonReader.TokenType)
            {
                case JsonToken.PropertyName:
                    var propertyName = jsonReader.Value.ToString();
                    await jsonReader.ReadAsync(); // Move to the property value.

                    if (propertyName == "type" && jsonReader.TokenType == JsonToken.String)
                    {
                        BundleType = jsonReader.Value.ToString();
                    }
                    else if (propertyName == "entry" && jsonReader.TokenType == JsonToken.StartArray)
                    {
                        while (await jsonReader.ReadAsync() && jsonReader.TokenType != JsonToken.EndArray)
                        {
                            if (jsonReader.TokenType == JsonToken.StartObject)
                            {
                                string entryResource = null;
                                string entryRequestMethod = null;

                                // Process each entry object
                                while (await jsonReader.ReadAsync() && jsonReader.TokenType != JsonToken.EndObject)
                                {
                                    if (jsonReader.TokenType == JsonToken.PropertyName)
                                    {
                                        var propName = jsonReader.Value.ToString();
                                        await jsonReader.ReadAsync(); // Move to the value of the property.

                                        if (propName == "resource")
                                        {
                                            // Bundle Resource string
                                            try
                                            {
                                                JObject obj = await JObject.LoadAsync(jsonReader);
                                                entryResource = obj.ToString(Formatting.None);
                                            }
                                            catch (Exception ex)
                                            {
                                                Debug.WriteLine(ex);
                                                throw;
                                            }
                                        }
                                        else if (propName == "request")
                                        {
                                            while (await jsonReader.ReadAsync() && jsonReader.TokenType != JsonToken.EndObject)
                                            {
                                                if (jsonReader.TokenType == JsonToken.PropertyName)
                                                {
                                                    var requestPropName = jsonReader.Value.ToString();
                                                    await jsonReader.ReadAsync(); // Move to the value of the property.

                                                    if (requestPropName == "method" && jsonReader.TokenType == JsonToken.String)
                                                    {
                                                        entryRequestMethod = jsonReader.Value.ToString();
                                                    }
                                                }
                                                else
                                                {
                                                    // Skip properties within the entry that are not "resource"
                                                    await jsonReader.SkipAsync();
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // Skip properties within the entry that are not "resource"
                                            await jsonReader.SkipAsync();
                                        }
                                    }
                                }

                                if (string.Equals("POST", entryRequestMethod, StringComparison.OrdinalIgnoreCase))
                                {
                                    throw new NotSupportedException("POST requests are not supported.");
                                }

                                if (!string.IsNullOrEmpty(entryResource))
                                {
                                    yield return entryResource;
                                }
                            }
                        }
                    }
                    else
                    {
                        // Capture unused properties
                        if (jsonReader.TokenType == JsonToken.String)
                        {
                            UnusedProperties.Add((propertyName, jsonReader.Value.ToString()));
                        }
                        else if (jsonReader.TokenType == JsonToken.StartObject || jsonReader.TokenType == JsonToken.StartArray)
                        {
                            // Skip objects or arrays that are not used.
                            await jsonReader.SkipAsync();
                        }
                    }

                    break;
            }
        }
    }

    public void Dispose()
    {
        streamReader?.Dispose();
        ((IDisposable)jsonReader)?.Dispose();
    }
}
