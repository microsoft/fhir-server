// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Covers the guard that checks the unit test leg still runs the projects it is meant to.
    /// </summary>
    /// <remarks>
    /// The unit test leg names no projects: it runs whatever <c>**/*UnitTests/*.csproj</c> matches.
    /// A project that leaves the glob is never run, every project that stayed passes, and the leg
    /// reports success. The script in
    /// <c>build/jobs/scripts/Assert-UnitTestProjectsDiscovered.ps1</c> is what turns that into a red
    /// leg, by checking the projects that matched against a list of the ones expected.
    /// </remarks>
    public class UnitTestProjectManifestTests
    {
        /// <summary>
        /// Every project the list names still exists where it says.
        /// </summary>
        /// <remarks>
        /// The leg's own copy of this check runs against the repository the pipeline cloned. This
        /// one runs against the working tree, so a rename that was not carried into the list is
        /// reported by the tests rather than only by CI.
        /// </remarks>
        [Fact]
        public void GivenTheExpectedProjectList_WhenReadAgainstTheRepository_ThenEveryProjectListedExists()
        {
            string manifest = ScriptRunner.Resolve("UnitTestProjectManifest");
            string root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(manifest))));

            string[] missing = ReadManifest(manifest)
                .Where(relative => !File.Exists(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))))
                .ToArray();

            Assert.Empty(missing);
        }

        /// <summary>
        /// The list is not empty, which is the one shape that would accept a leg running nothing.
        /// </summary>
        [Fact]
        public void GivenTheExpectedProjectList_WhenRead_ThenItNamesProjects()
        {
            Assert.NotEmpty(ReadManifest(ScriptRunner.Resolve("UnitTestProjectManifest")));
        }

        /// <summary>
        /// A tree holding every expected project.
        /// </summary>
        [Fact]
        public void GivenEveryExpectedProject_WhenChecked_ThenTheLegIsLeftGreen()
        {
            using var tree = new ProjectTree();

            ScriptRun run = tree.Check();

            Assert.Equal(0, run.ExitCode);
        }

        /// <summary>
        /// One project renamed and another added, which leaves the number of projects unchanged.
        /// </summary>
        /// <remarks>
        /// This is the case a count cannot see, and it is not a hypothetical one: renaming a project
        /// and adding a project are both ordinary changes, and they only have to land in the same
        /// pull request. The renamed project stops being tested and the count still adds up.
        /// </remarks>
        [Fact]
        public void GivenAProjectRenamedWhileAnotherIsAdded_WhenChecked_ThenTheRenamedProjectIsReported()
        {
            using var tree = new ProjectTree();

            string renamed = tree.Remove(0);
            tree.Add("src/Some.Brand.New.UnitTests/Some.Brand.New.UnitTests.csproj");

            ScriptRun run = tree.Check();

            Assert.Equal(1, run.ExitCode);
            Assert.Contains(renamed, run.Output, StringComparison.Ordinal);
        }

        /// <summary>
        /// A project added without being listed, which still runs and must not fail the leg.
        /// </summary>
        [Fact]
        public void GivenAnUnlistedProject_WhenChecked_ThenTheLegIsLeftGreenAndTheProjectIsNamed()
        {
            using var tree = new ProjectTree();

            tree.Add("src/Some.Brand.New.UnitTests/Some.Brand.New.UnitTests.csproj");

            ScriptRun run = tree.Check();

            Assert.Equal(0, run.ExitCode);
            Assert.Contains("Some.Brand.New.UnitTests", run.Output, StringComparison.Ordinal);
        }

        /// <summary>
        /// A project whose directory suffix changed, so the glob no longer matches it.
        /// </summary>
        [Fact]
        public void GivenAProjectDirectoryNoLongerEndingInUnitTests_WhenChecked_ThenItIsReported()
        {
            using var tree = new ProjectTree();

            string moved = tree.Remove(0);
            tree.Add(moved.Replace("UnitTests/", "Tests/", StringComparison.Ordinal));

            ScriptRun run = tree.Check();

            Assert.Equal(1, run.ExitCode);
            Assert.Contains(moved, run.Output, StringComparison.Ordinal);
        }

        /// <summary>
        /// A list naming nothing, which would otherwise accept any tree at all.
        /// </summary>
        [Fact]
        public void GivenAnEmptyExpectedProjectList_WhenChecked_ThenTheLegIsFailed()
        {
            using var tree = new ProjectTree();

            tree.OverwriteManifest("# every line here is a comment");

            ScriptRun run = tree.Check();

            Assert.Equal(1, run.ExitCode);
            Assert.Contains("names no projects", run.Output, StringComparison.Ordinal);
        }

        private static IReadOnlyList<string> ReadManifest(string path)
            => File.ReadAllLines(path)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .ToArray();

        /// <summary>
        /// A directory holding a project file for each entry of the real expected project list.
        /// </summary>
        private sealed class ProjectTree : IDisposable
        {
            private readonly string _root = Path.Combine(Path.GetTempPath(), "xunit-ext-projects", Guid.NewGuid().ToString("N"));
            private readonly List<string> _projects;
            private string _manifest;

            public ProjectTree()
            {
                _manifest = ScriptRunner.Resolve("UnitTestProjectManifest");
                _projects = ReadManifest(_manifest).ToList();

                foreach (string project in _projects)
                {
                    Add(project);
                }
            }

            public void Add(string relativePath)
            {
                string full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllText(full, "<Project />");
            }

            /// <summary>
            /// Deletes one of the expected projects and returns the path it was listed under.
            /// </summary>
            public string Remove(int index)
            {
                string relativePath = _projects[index];
                Directory.Delete(Path.GetDirectoryName(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar))), recursive: true);
                return relativePath;
            }

            /// <summary>
            /// Replaces the list the check reads with one written for a single test.
            /// </summary>
            public void OverwriteManifest(string contents)
            {
                _manifest = Path.Combine(_root, "expected-projects.txt");
                File.WriteAllText(_manifest, contents);
            }

            public ScriptRun Check()
                => ScriptRunner.Run(
                    ScriptRunner.Resolve("UnitTestProjectsScript"),
                    new Dictionary<string, string>
                    {
                        ["SourcesDirectory"] = _root,
                        ["ManifestPath"] = _manifest,
                    });

            public void Dispose()
            {
                try
                {
                    Directory.Delete(_root, recursive: true);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // A leaked temp directory is not worth failing an otherwise good test over.
                }
            }
        }
    }
}
