// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Runs one of the repository's CI guard scripts the way the pipeline runs it.
    /// </summary>
    /// <remarks>
    /// Two of the guards that decide whether a test leg is allowed to report success are PowerShell
    /// scripts rather than tests, so the only way to assert on what they do is to run them. They are
    /// run from their place in the repository rather than from a copy, so what these tests cover is
    /// what the pipeline invokes.
    /// </remarks>
    internal static class ScriptRunner
    {
        private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Runs a script and returns its exit code together with everything it wrote.
        /// </summary>
        /// <param name="scriptPath">The script to run.</param>
        /// <param name="parameters">The parameters to pass, by name without the leading dash.</param>
        public static ScriptRun Run(string scriptPath, IReadOnlyDictionary<string, string> parameters)
        {
            var startInfo = new ProcessStartInfo("pwsh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            // The pipeline runs these scripts with pwsh set on every task that calls them, and they
            // say so themselves with a #requires line. Windows PowerShell cannot parse them, so
            // running whatever "powershell" happens to mean here would test something no leg runs.
            //
            // They are invoked through -Command rather than -File so that a failure message can be
            // read back as the script wrote it: PowerShell's default error view draws a box around a
            // terminating error and rewraps the text inside it to the console width, which would
            // leave assertions matching on how wide a machine's console happens to be. The exit code
            // is unaffected by the view, and is what the leg actually reads.
            var command = new StringBuilder("$ErrorView='NormalView'; & '").Append(scriptPath).Append('\'');

            foreach (KeyValuePair<string, string> parameter in parameters)
            {
                command.Append(" -").Append(parameter.Key).Append(" '").Append(parameter.Value).Append('\'');
            }

            command.Append("; exit $LASTEXITCODE");

            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(command.ToString());

            using var process = new Process { StartInfo = startInfo };
            using var outputComplete = new ManualResetEventSlim(false);
            using var errorComplete = new ManualResetEventSlim(false);

            var output = new StringBuilder();

            // Both streams are drained as they arrive rather than one after the other. Reading one
            // stream to the end first deadlocks as soon as the script writes more to the other than
            // its pipe holds, which is not a hypothetical: these scripts report what went wrong by
            // naming every test or project involved, on the error stream, while the same run is
            // writing what it matched to the output stream. The wait below cannot rescue that,
            // because it is only reached once the reads have finished.
            void Append(DataReceivedEventArgs e, ManualResetEventSlim complete)
            {
                if (e.Data == null)
                {
                    try
                    {
                        complete.Set();
                    }
                    catch (ObjectDisposedException)
                    {
                        // The only way this event is disposed while a callback is still in flight is
                        // that this method has already given up waiting and is unwinding. Letting the
                        // exception escape would take down the test host from a thread pool thread.
                    }
                }
                else
                {
                    lock (output)
                    {
                        output.AppendLine(e.Data);
                    }
                }
            }

            process.OutputDataReceived += (_, e) => Append(e, outputComplete);
            process.ErrorDataReceived += (_, e) => Append(e, errorComplete);

            try
            {
                process.Start();
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                throw new InvalidOperationException(
                    "PowerShell 7 ('pwsh') was not found on this machine. The CI legs run this script with it, so these tests need it to run the same thing the legs do.",
                    e);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            bool exited = process.WaitForExit((int)RunTimeout.TotalMilliseconds);

            // Waiting for the process to exit is not the same as having been handed everything it
            // wrote: the timed overload of WaitForExit returns as soon as the process is gone, while
            // the handlers above are still being called on other threads. Reading the output without
            // waiting for them yields whatever happened to have arrived, which on a loaded machine
            // is nothing at all, and turns a passing script into an assertion about an empty string.
            if (exited)
            {
                outputComplete.Wait(RunTimeout);
                errorComplete.Wait(RunTimeout);
            }
            else
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // The child can exit on its own between the wait giving up and the kill landing.
                }

                lock (output)
                {
                    throw new InvalidOperationException($"'{scriptPath}' did not finish within {RunTimeout}. It reported:{Environment.NewLine}{output}");
                }
            }

            lock (output)
            {
                return new ScriptRun(process.ExitCode, output.ToString());
            }
        }

        /// <summary>
        /// Resolves a path the project file recorded in assembly metadata.
        /// </summary>
        /// <param name="metadataKey">The metadata key holding the path.</param>
        public static string Resolve(string metadataKey)
        {
            string path = typeof(ScriptRunner).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == metadataKey)
                ?.Value;

            if (string.IsNullOrEmpty(path))
            {
                throw new InvalidOperationException(
                    $"The {metadataKey} metadata attribute is missing. It is injected by the project file and identifies a file these tests run or read.");
            }

            // The project file writes this path with the separator MSBuild used on the machine that
            // built it, and a backslash is an ordinary filename character everywhere except Windows.
            path = Path.GetFullPath(path.Replace('\\', Path.DirectorySeparatorChar));

            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"The file recorded as {metadataKey} was not found at '{path}'.");
            }

            return path;
        }
    }
}
