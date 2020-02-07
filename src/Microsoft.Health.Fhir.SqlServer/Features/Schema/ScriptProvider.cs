// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.Reflection;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema
{
    public static class ScriptProvider
    {
        public static string GetMigrationScript(int version, bool applyFullSchemaSnapshot)
        {
            string folder = $"{typeof(ScriptProvider).Namespace}.Migrations";
            string resourceName = applyFullSchemaSnapshot ? $"{folder}.{version}.sql" : $"{folder}.{version}.diff.sql";

            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new FileNotFoundException(Resources.ScriptNotFound);
                }

                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        public static byte[] GetMigrationScriptAsBytes(int version)
        {
            string resourceName = $"{typeof(ScriptProvider).Namespace}.Migrations.{version}.sql";
            using (Stream filestream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (filestream == null)
                {
                    throw new FileNotFoundException(Resources.ScriptNotFound);
                }

                byte[] scriptBytes = new byte[filestream.Length];
                filestream.Read(scriptBytes, 0, scriptBytes.Length);
                return scriptBytes;
            }
        }
    }
}
