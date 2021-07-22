// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;

namespace Microsoft.Health.Fhir.Core.Features.GraphQl
{
    public static class GraphQlLoader
    {
        private const string Base = "../Microsoft.Health.Fhir.Core/Data/GraphQl/";

        public static string GetDefinition(string fileName)
        {
            var schema = File.ReadAllText(Base + fileName + ".graphql");

            return schema;
        }
    }
}
