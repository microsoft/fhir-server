// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.Models
{
    /// <summary>
    /// A custom converter for de-serializing the Output property in ExportJobRecord correctly.
    /// In SchemaVersion v1 for EJR, Output is of Dictionary<string, ExportFileInfo> format.
    /// In SchemaVersion v2 it is of Dictionary<string, List<ExportFileInfo>> format.
    /// This converter makes sure the updated code can still read v1 by returning a
    /// List<ExportFileInfo> always.
    /// </summary>
    public class ExportJobRecordOutputConverter : JsonConverter
    {
        public override bool CanWrite => false;

        public override bool CanConvert(Type objectType)
        {
            if (objectType == typeof(Dictionary<string, List<ExportFileInfo>>))
            {
                return true;
            }

            return false;
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JToken jsonData = JToken.ReadFrom(reader);
            List<ExportFileInfo> result = new List<ExportFileInfo>();

            // Check if it is an array or single object and de-serialize accordingly.
            if (jsonData is JArray)
            {
                JArray array = jsonData as JArray;
                foreach (JObject entry in array)
                {
                    ExportFileInfo fileInfo = serializer.Deserialize<ExportFileInfo>(entry.CreateReader());
                    result.Add(fileInfo);
                }
            }
            else if (jsonData is JObject)
            {
                ExportFileInfo fileInfo = serializer.Deserialize<ExportFileInfo>(jsonData.CreateReader());
                result.Add(fileInfo);
            }

            return result;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            // This will never be called since we have CanWrite set to false.
            throw new NotImplementedException();
        }
    }
}
