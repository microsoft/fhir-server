// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Diagnostics;

namespace Microsoft.Health.Fhir.SqlServer.Database
{
    public static class SetupDb
    {
        public static void Publish(string connectionString, string dacpac)
        {
            var param = $"/C sqlpackage.exe /Action:Publish /SourceFile:\"{dacpac}\" /TargetConnectionString:\"{connectionString}\" /p:AllowDropBlockingAssemblies=true /p:NoAlterStatementsToChangeClrTypes=true /p:AllowIncompatiblePlatform=true /p:IncludeCompositeObjects=true";
            RunOsCommand("cmd.exe ", param, true);
        }

        private static void RunOsCommand(string filename, string arguments, bool redirectOutput)
        {
            var processStartInfo = new ProcessStartInfo(filename, arguments)
            {
                UseShellExecute = !redirectOutput,
                RedirectStandardError = redirectOutput, // if redirected then parallel perf drops
                RedirectStandardOutput = redirectOutput,
                CreateNoWindow = false // make everything visible and killable
            };

            using var process = Process.Start(processStartInfo);
            if (process == null)
            {
                throw new ArgumentException("Process start information wasn't successfully created.");
            }

            if (redirectOutput)
            {
                process.OutputDataReceived += (sender, args) => Console.WriteLine(args.Data);
                process.BeginOutputReadLine();
                process.ErrorDataReceived += (sender, args) => Console.WriteLine(args.Data);
                process.BeginErrorReadLine();
            }

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new ArgumentException($"Error {process.ExitCode} running {filename}.");
            }
        }
    }
}
