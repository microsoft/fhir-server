// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#pragma warning disable CA2100
#pragma warning disable CA1303
using System.Configuration;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer;

namespace Microsoft.Health.Internal.Fhir.SqlScriptRunner
{
    public static class Program
    {
        private static readonly string _connectionString = ConfigurationManager.ConnectionStrings["Database"].ConnectionString;
        private static readonly string _script = ConfigurationManager.AppSettings["Script"];
        private static readonly bool _execute = bool.Parse(ConfigurationManager.AppSettings["Execute"]);
        private static string _newLine = "\r";
        private static SqlRetryService _sqlRetryService;

        public static void Main()
        {
            Console.WriteLine($"{DateTime.UtcNow.ToString("s")}: script = {_script}");
            var script = File.ReadAllText(_script);
            ISqlConnectionBuilder iSqlConnectionBuilder = new Sql.SqlConnectionBuilder(_connectionString);
            _sqlRetryService = SqlRetryService.GetInstance(iSqlConnectionBuilder);
            var scriptSections = script.Split($"{_newLine}GO{_newLine}");
            foreach (var section in scriptSections)
            {
                var firstLine = section.Substring(0, 50).Replace(_newLine, Environment.NewLine, StringComparison.InvariantCulture);
                Console.WriteLine($"{DateTime.UtcNow.ToString("s")}: starting...");
                Console.WriteLine(firstLine);
                Console.WriteLine();
                if (_execute)
                {
                    using var cmd = new SqlCommand() { CommandText = section, CommandTimeout = 0 };
                    cmd.ExecuteNonQueryAsync(_sqlRetryService, NullLogger<Sql.SqlConnectionBuilder>.Instance, CancellationToken.None).Wait();
                }
                else
                {
                    Console.WriteLine("---------------------------------------------------------------------------");
                    Console.WriteLine(section.Replace(_newLine, Environment.NewLine, StringComparison.InvariantCulture));
                    Console.WriteLine("---------------------------------------------------------------------------");
                }

                Console.WriteLine($"{DateTime.UtcNow.ToString("s")}: completed.");
            }
        }
    }
}
