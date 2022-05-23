// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal static class MissingResourceFactory
    {
        public static string CreateJson(string resourceId, string resourceType, string severity, string code)
        {
            return @$"{{
  ""resourceType"": ""OperationOutcome"",
  ""id"": ""{resourceId}"",
  ""issue"": [
    {{
      ""severity"": ""{severity}"",
      ""code"": ""{code}"",
      ""diagnostics"": ""{string.Format(Resources.ResourceNotAvailable, resourceType, resourceId)}"",
      ""expression"": [
        ""{resourceType}""
      ]
    }}
  ]
}}";
        }
    }
}
