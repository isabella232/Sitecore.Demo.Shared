﻿using Cake.Common.IO;
using Cake.Common.Tools.MSBuild;
using Cake.Core;
using Cake.Core.Annotations;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Http;
using System;
using System.Linq;
using System.Text.RegularExpressions;
[assembly: CakeNamespaceImport("System.Text.RegularExpressions")]

/*===============================================
================= HELPER METHODS ================
===============================================*/

namespace Cake.SitecoreDemo
{
    public static class CakeHelper
    {
        [CakeMethodAlias]
        public static void PrintHeader(this ICakeContext context, CakeConsole cakeConsole, ConsoleColor foregroundColor)
        {
            cakeConsole.WriteLine("Sitecore Demo Build Tasks");
            cakeConsole.ResetColor();
        }

        [CakeMethodAlias]
        public static void PublishProjects(this ICakeContext context, Configuration config, string rootFolder, string destination)
        {
            context.PublishProjects(config, rootFolder, destination, new string[] {});
        }

        [CakeMethodAlias]
        public static void PublishProjects(this ICakeContext context, Configuration config, string rootFolder, string destination, string[] excludePatterns)
        {
            context.PublishProjects(config, rootFolder, destination, excludePatterns, "code");
        }

        [CakeMethodAlias]
        public static void PublishProjects(this ICakeContext context, Configuration config, string rootFolder, string destination, string[] excludePatterns, string projectParentFolderName)
        {
            var globberSettings = new GlobberSettings();
            bool excludes(IFileSystemInfo fileSystemInfo) => !excludePatterns.Any(s => fileSystemInfo.Path.FullPath.Contains(s));
            globberSettings.FilePredicate = excludes;

            var projects = GlobbingAliases.GetFiles(context, $"{rootFolder}\\**\\{projectParentFolderName}\\*.csproj", globberSettings);
            context.Log.Information("Publishing " + rootFolder + " to " + destination);

            foreach (var project in projects)
            {
                context.MSBuild(project, cfg => InitializeMSBuildSettingsInternal(context, config, cfg)
                  .WithTarget(config.BuildTargets)
                  .WithProperty("DeployOnBuild", "true")
                  .WithProperty("DeployDefaultTarget", "WebPublish")
                  .WithProperty("WebPublishMethod", "FileSystem")
                  .WithProperty("DeleteExistingFiles", "false")
                  .WithProperty("publishUrl", destination)
                  .WithProperty("BuildProjectReferences", "false")
                  );
            }
        }

        [CakeMethodAlias]
        public static FilePathCollection GetTransformFiles(this ICakeContext context, string rootFolder)
        {
            var globberSettings = new GlobberSettings();
            bool exclude_obj_bin_folder(IFileSystemInfo fileSystemInfo) => !fileSystemInfo.Path.FullPath.Contains("/obj/") && !fileSystemInfo.Path.FullPath.Contains("/bin/");
            globberSettings.FilePredicate = exclude_obj_bin_folder;

            var xdtFiles = context.GetFiles($"{rootFolder}\\**\\*.xdt", globberSettings);
            return xdtFiles;
        }

        [CakeMethodAlias]
        public static void Transform(this ICakeContext context, string rootFolder, string filter, string publishDestination, string[] excludePatterns)
        {
            var xdtFiles = GetTransformFiles(context, rootFolder);

            foreach (var file in xdtFiles)
            {

                if (excludePatterns.Any(s => file.FullPath.ToLower().Contains(s.ToLower())))
                {
                    context.Log.Information($"Skipping {file}");
                    continue;
                }
                context.Log.Information($"Applying configuration transform:{file.FullPath}");
                var fileToTransform = Regex.Replace(file.FullPath, $".+{filter}/(.*.config).?(.*).xdt", "$1");
                fileToTransform = Regex.Replace(fileToTransform, ".sc-internal", "");
                var sourceTransform = $"{publishDestination}\\{fileToTransform}";

                XdtTransform.XdtTransformationAlias.XdtTransformConfig(context,
                            sourceTransform      // Source File
                          , file.FullPath       // Tranforms file (*.xdt)
                          , sourceTransform);   // Target File
            }
        }

        [CakeMethodAlias]
        public static void RebuildIndex(this ICakeContext context, Configuration config, string indexName)
        {
            var url = $"{config.InstanceUrl}/utilities/indexrebuild.aspx?index={indexName}";
            string responseBody = context.HttpGet(url);
        }

        [CakeMethodAlias]
        public static void DeployExmCampaigns(this ICakeContext context, Configuration config)
        {
            var url = $"{config.InstanceUrl}/utilities/deployemailcampaigns.aspx?apiKey={config.MessageStatisticsApiKey}";
            var responseBody = context.HttpGet(url, settings =>
            {
                settings.AppendHeader("Connection", "keep-alive");
            });
            context.Log.Information(responseBody);
        }

        [CakeMethodAlias]
        public static MSBuildSettings InitializeMSBuildSettings(this ICakeContext context,Configuration config, MSBuildSettings settings)
        {
            InitializeMSBuildSettingsInternal(context, config, settings)
              .WithRestore();
            return settings;
        }

        private static MSBuildSettings InitializeMSBuildSettingsInternal(this ICakeContext context, Configuration config, MSBuildSettings settings)
        {
            settings.SetConfiguration(config.BuildConfiguration)
              .SetVerbosity(Verbosity.Minimal)
              .SetMSBuildPlatform(MSBuildPlatform.Automatic)
              .SetPlatformTarget(PlatformTarget.MSIL)
              .UseToolVersion(config.MSBuildToolVersion)
              .SetMaxCpuCount(1);
            return settings;
        }

        [CakeMethodAlias]
        public static void CreateFolder(this ICakeContext context, string folderPath)
        {
            if (!context.DirectoryExists(folderPath))
            {
                context.CreateDirectory(folderPath);
            }
        }

        [CakeMethodAlias]
        public static void Spam(this ICakeContext context, Action action, int? timeoutMinutes = null)
        {
            Exception lastException = null;
            var startTime = DateTime.Now;
            while (timeoutMinutes == null || (DateTime.Now - startTime).TotalMinutes < timeoutMinutes)
            {
                try
                {
                    action();

                    context.Log.Information($"Completed in {(DateTime.Now - startTime).Minutes} min {(DateTime.Now - startTime).Seconds} sec.");
                    return;
                }
                catch (AggregateException aex)
                {
                    foreach (var x in aex.InnerExceptions)
                    {
                        context.Log.Information($"{x.GetType().FullName}: {x.Message}");
                    }
                    lastException = aex;
                }
                catch (Exception ex)
                {
                    context.Log.Information($"{ex.GetType().FullName}: {ex.Message}");
                    lastException = ex;
                }
            }

            throw new TimeoutException($"Unable to complete within {timeoutMinutes} minutes.", lastException);
        }

        [CakeMethodAlias]
        public static void WriteError(this ICakeContext context, string errorMessage)
        {
            context.Log.Error(errorMessage);
        }

        [CakeMethodAlias]
        public static void MergeTransforms(this ICakeContext context, string source, string projectParentFolderName, string destination, string[] excludePatterns)
        {
            var xdtFiles = GetTransformFiles(context, source);

            foreach (var file in xdtFiles)
            {
                if (excludePatterns.Any(s => file.FullPath.ToLower().Contains(s.ToLower())))
                {
                    context.Log.Information($"Skipping {file}");
                    continue;
                }

                FilePath xdtFilePath = (FilePath)file;
                context.Log.Information($"Processing {xdtFilePath}");

                FilePath targetTransformPath;
                if (string.IsNullOrEmpty(projectParentFolderName))
                {
                    // For [PublishTempFolder]\transforms\**\*.config.*.xdt to [destination]\transforms\**\*.config.xdt
                    FilePath fileToTransform = Regex.Replace(file.FullPath, "(.*.config).?(.*)", "$1.xdt");
                    fileToTransform = ((FilePath)$"{source}").GetRelativePath((FilePath)fileToTransform);
                    FilePath sourceTransform = $"{(FilePath)fileToTransform}";
                    targetTransformPath = ((DirectoryPath)destination).CombineWithFilePath((FilePath)sourceTransform);
                }
                else
                {
                    // For [Feature]\[Module]\[projectParentFolderName]\**\*.config.*.xdt to [destination]\transforms\**\*.config.xdt
                    var fileToTransform = Regex.Replace(file.FullPath, $".+{projectParentFolderName}/(.*.config).?(.*)", "transforms\\$1.xdt");
                    targetTransformPath = ((DirectoryPath)destination).CombineWithFilePath((FilePath)fileToTransform);
                }

                context.Log.Information($"Target transform path: {targetTransformPath}");

                if (!context.FileExists(targetTransformPath))
                {
                    CreateFolder(context, targetTransformPath.GetDirectory().FullPath);
                    context.Log.Information($"Copying: {xdtFilePath.FullPath} to {targetTransformPath}");

                    context.CopyFile(xdtFilePath.FullPath, targetTransformPath);
                }
                else
                {
                    CakeXmlHelper.MergeFile(
                        targetTransformPath.FullPath, // Source File
                        xdtFilePath.FullPath,         // Transforms file (*.xdt)
                        targetTransformPath.FullPath  // Target File
                    );
                }
            }
        }
    }
}
