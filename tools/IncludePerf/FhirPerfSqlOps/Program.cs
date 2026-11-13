// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;
using Microsoft.Data.SqlClient;

// Runs T-SQL against Azure SQL using an Entra ID access token. The subscription policy forces
// Entra-only authentication (no SQL logins), so token auth is the only option. Used to grant the
// container apps' managed identity access to the shared database and to pull Query Store statistics
// after a benchmark run.
string server = null;
string database = "master";
string query = null;
string inputFile = null;
string outputPath = null;
bool asJson = false;
int timeoutSeconds = 900;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i].ToLowerInvariant())
    {
        case "--server":
            server = args[++i];
            break;
        case "--database":
            database = args[++i];
            break;
        case "--query":
            query = args[++i];
            break;
        case "--file":
            inputFile = args[++i];
            break;
        case "--output":
            outputPath = args[++i];
            break;
        case "--json":
            asJson = true;
            break;
        case "--timeout":
            timeoutSeconds = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--help":
        case "-h":
            Console.WriteLine("Usage: FhirPerfSqlOps --server <fqdn> --database <db> (--query <sql> | --file <path>)");
            Console.WriteLine("       [--json] [--output <file>] [--timeout <seconds>]");
            return 0;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            return 1;
    }
}

if (string.IsNullOrWhiteSpace(server))
{
    Console.Error.WriteLine("--server is required.");
    return 1;
}

if (inputFile != null)
{
    query = File.ReadAllText(inputFile);
}

if (string.IsNullOrWhiteSpace(query))
{
    Console.Error.WriteLine("Provide --query or --file.");
    return 1;
}

var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
{
    ExcludeInteractiveBrowserCredential = false,
});

string[] sqlScope = { "https://database.windows.net/.default" };

AccessToken accessToken = await credential.GetTokenAsync(
    new TokenRequestContext(sqlScope),
    CancellationToken.None);

var builder = new SqlConnectionStringBuilder
{
    DataSource = $"tcp:{server},1433",
    InitialCatalog = database,
    Encrypt = SqlConnectionEncryptOption.Mandatory,
    TrustServerCertificate = false,
    ConnectTimeout = 60,
};

await using var connection = new SqlConnection(builder.ConnectionString) { AccessToken = accessToken.Token };
await connection.OpenAsync();

// GO is a client-side batch separator, not T-SQL. Split so scripts behave like sqlcmd.
string[] batches = Regex.Split(query, @"(?im)^\s*GO\s*$")
    .Where(b => !string.IsNullOrWhiteSpace(b))
    .ToArray();

var allResults = new List<List<Dictionary<string, object>>>();

foreach (string batch in batches)
{
    await using SqlCommand command = connection.CreateCommand();
    command.CommandText = batch;
    command.CommandTimeout = timeoutSeconds;

    await using SqlDataReader reader = await command.ExecuteReaderAsync();

    do
    {
        if (reader.FieldCount == 0)
        {
            continue;
        }

        var rows = new List<Dictionary<string, object>>();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object>(StringComparer.Ordinal);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                object value = reader.GetValue(i);
                row[reader.GetName(i)] = value is DBNull ? null : value;
            }

            rows.Add(row);
        }

        if (rows.Count > 0)
        {
            allResults.Add(rows);
        }
    }
    while (await reader.NextResultAsync());
}

string rendered = asJson || outputPath != null
    ? JsonSerializer.Serialize(
        allResults.Count == 1 ? (object)allResults[0] : allResults,
        new JsonSerializerOptions { WriteIndented = true })
    : RenderTables(allResults);

if (outputPath != null)
{
    File.WriteAllText(outputPath, rendered);
    Console.WriteLine($"Wrote {outputPath}");
}
else
{
    Console.WriteLine(rendered);
}

return 0;

static string RenderTables(List<List<Dictionary<string, object>>> results)
{
    if (results.Count == 0)
    {
        return "(no rows)";
    }

    var sb = new StringBuilder();

    foreach (List<Dictionary<string, object>> rows in results)
    {
        string[] columns = rows[0].Keys.ToArray();
        var widths = columns.ToDictionary(
            c => c,
            c => Math.Max(c.Length, rows.Max(r => Format(r[c]).Length)),
            StringComparer.Ordinal);

        sb.AppendLine(string.Join("  ", columns.Select(c => c.PadRight(widths[c]))));
        sb.AppendLine(string.Join("  ", columns.Select(c => new string('-', widths[c]))));

        foreach (Dictionary<string, object> row in rows)
        {
            sb.AppendLine(string.Join("  ", columns.Select(c => Format(row[c]).PadRight(widths[c]))));
        }

        sb.AppendLine();
    }

    return sb.ToString();
}

static string Format(object value) => value switch
{
    null => "NULL",
    DateTime dt => dt.ToString("s", CultureInfo.InvariantCulture),
    IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
    _ => value.ToString(),
};
