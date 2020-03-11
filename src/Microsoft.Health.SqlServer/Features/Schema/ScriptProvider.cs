// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Reflection;

namespace Microsoft.Health.SqlServer.Features.Schema
{
    public static class ScriptProvider
    {
        public static string GetMigrationScript<TSchemaVersionEnum>(int version, bool applyFullSchemaSnapshot)
            where TSchemaVersionEnum : Enum
        {
            string folder = $"{typeof(TSchemaVersionEnum).Namespace}.Migrations";
            string resourceName = applyFullSchemaSnapshot ? $"{folder}.{version}.sql" : $"{folder}.{version}.diff.sql";

            using (Stream stream = Assembly.GetAssembly(typeof(TSchemaVersionEnum)).GetManifestResourceStream(resourceName))
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

        public static byte[] GetMigrationScriptAsBytes<TSchemaVersionEnum>(int version)
            where TSchemaVersionEnum : Enum
        {
            string resourceName = $"{typeof(TSchemaVersionEnum).Namespace}.Migrations.{version}.sql";
            using (Stream fileStream = Assembly.GetAssembly(typeof(TSchemaVersionEnum)).GetManifestResourceStream(resourceName))
            {
                if (fileStream == null)
                {
                    throw new FileNotFoundException(Resources.ScriptNotFound);
                }

                var scriptBytes = new byte[fileStream.Length];
                fileStream.Read(scriptBytes, 0, scriptBytes.Length);
                return scriptBytes;
            }
        }
    }
}
