﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.NET.TestFramework.Commands;
using System.Reflection;

namespace Microsoft.NET.TestFramework
{
    public class TestContext
    {
        //  Generally the folder the test DLL is in
        public string TestExecutionDirectory { get; set; }

        public string TestAssetsDirectory { get; set; }

        public string NuGetCachePath { get; set; }

        public string NuGetFallbackFolder { get; set; }

        public string NuGetExePath { get; set; }

        public string SdkVersion { get; set; }

        public ToolsetInfo ToolsetUnderTest { get; set; }

        private static TestContext _current;

        public static TestContext Current
        {
            get
            {
                if (_current == null)
                {
                    //  Initialize test context in cases where it hasn't been initialized via the entry point
                    //  (ie when using test explorer or another runner)
                    Initialize(TestCommandLine.Parse(Array.Empty<string>()));
                }
                return _current;
            }
            set
            {
                _current = value;
            }
        }

        public const string LatestRuntimePatchForNetCoreApp2_0 = "2.0.9";

        public void AddTestEnvironmentVariables(SdkCommandSpec command)
        {
            command.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";

            //  Set NUGET_PACKAGES environment variable to match value from build.ps1
            command.Environment["NUGET_PACKAGES"] = NuGetCachePath;

            command.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";

            command.Environment["GenerateResourceMSBuildArchitecture"] = "CurrentArchitecture";
            command.Environment["GenerateResourceMSBuildRuntime"] = "CurrentRuntime";

            //  Prevent test MSBuild nodes from persisting
            command.Environment["MSBUILDDISABLENODEREUSE"] = "1";

            ToolsetUnderTest.AddTestEnvironmentVariables(command);
        }


        public static void Initialize(TestCommandLine commandLine)
        {
            Environment.SetEnvironmentVariable("DOTNET_MULTILEVEL_LOOKUP", "0");

            TestContext testContext = new TestContext();
            
            bool runAsTool = false;
            if (Directory.Exists(Path.Combine(AppContext.BaseDirectory, "Assets")))
            {
                runAsTool = true;
                testContext.TestAssetsDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "TestProjects");
            }
            else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_SDK_TEST_AS_TOOL")))
            {
                //  Pretend to run as a tool, but use the test assets found in the repo
                //  This allows testing most of the "tests as global tool" behavior by setting an environment
                //  variable instead of packing the test, and installing it as a global tool.
                runAsTool = true;
                
                testContext.TestAssetsDirectory = FindFolderInTree(Path.Combine("src", "Assets", "TestProjects"), AppContext.BaseDirectory);
            }

            if (runAsTool)
            {
                testContext.TestExecutionDirectory = Path.Combine(Path.GetTempPath(), "dotnetSdkTests");
                
            }
            else
            {
                // This is dependent on the current artifacts layout:
                // * $(RepoRoot)/artifacts/$(Configuration)/tmp
                // * $(RepoRoot)/artifacts/$(Configuration)/bin/Tests/$(MSBuildProjectName)
                testContext.TestExecutionDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "tmp"));

                testContext.TestAssetsDirectory = FindFolderInTree(Path.Combine("src", "Assets", "TestProjects"), AppContext.BaseDirectory);
            }

            string repoRoot = null;
            string repoConfiguration = null;

            if (commandLine.SDKRepoPath != null)
            {
                repoRoot = commandLine.SDKRepoPath;
            }
            else if (!commandLine.NoRepoInference && !runAsTool)
            {
                repoRoot = GetRepoRoot();

                if (repoRoot != null)
                {
                    // assumes tests are always executed from the "artifacts/$Configuration/bin/Tests/$MSBuildProjectFile" directory
                    repoConfiguration = new DirectoryInfo(AppContext.BaseDirectory).Parent.Parent.Parent.Name;
                }
            }

            string artifactsDir = Environment.GetEnvironmentVariable("DOTNET_SDK_ARTIFACTS_DIR");
            if (string.IsNullOrEmpty(artifactsDir) && !string.IsNullOrEmpty(repoRoot))
            {
                artifactsDir = Path.Combine(repoRoot, "artifacts");
            }

            if (repoRoot != null)
            {
                testContext.NuGetFallbackFolder = Path.Combine(artifactsDir, ".nuget", "NuGetFallbackFolder");
                testContext.NuGetExePath = Path.Combine(artifactsDir, ".nuget", $"nuget{Constants.ExeSuffix}");
                testContext.NuGetCachePath = Path.Combine(artifactsDir, ".nuget", "packages");
            }
            else
            {
                var nugetFolder = FindFolderInTree(".nuget", AppContext.BaseDirectory, false)
                    ?? Path.Combine(testContext.TestExecutionDirectory, ".nuget");
                

                testContext.NuGetFallbackFolder = Path.Combine(nugetFolder, "NuGetFallbackFolder");
                testContext.NuGetExePath = Path.Combine(nugetFolder, $"nuget{Constants.ExeSuffix}");
                testContext.NuGetCachePath = Path.Combine(nugetFolder, "packages");
            }

            if (commandLine.SdkVersion != null)
            {
                testContext.SdkVersion = commandLine.SdkVersion;
            }

            testContext.ToolsetUnderTest = ToolsetInfo.Create(repoRoot, artifactsDir, repoConfiguration, commandLine);

            TestContext.Current = testContext;
        }

        private static string GetRepoRoot()
        {
            string directory = AppContext.BaseDirectory;

            while (!Directory.Exists(Path.Combine(directory, ".git")) && directory != null)
            {
                directory = Directory.GetParent(directory).FullName;
            }

            if (directory == null)
            {
                return null;
            }
            return directory;
        }
        private static string FindOrCreateFolderInTree(string relativePath, string startPath)
        {
            string ret = FindFolderInTree(relativePath, startPath, throwIfNotFound: false);
            if (ret != null)
            {
                return ret;
            }
            ret = Path.Combine(startPath, relativePath);
            Directory.CreateDirectory(ret);
            return ret;
        }
        private static string FindFolderInTree(string relativePath, string startPath, bool throwIfNotFound = true)
        {
            string currentPath = startPath;
            while (true)
            {
                string path = Path.Combine(currentPath, relativePath);
                if (Directory.Exists(path))
                {
                    return path;
                }
                var parent = Directory.GetParent(currentPath);
                if (parent == null)
                {
                    if (throwIfNotFound)
                    {
                        throw new FileNotFoundException($"Could not find folder '{relativePath}' in '{startPath}' or any of its ancestors");
                    }
                    else
                    {
                        return null;
                    }
                }
                currentPath = parent.FullName;
            }
        }
    }
}
