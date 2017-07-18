﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using Calamari.Integration.FileSystem;
using Calamari.Integration.Packages.NuGet;
using Calamari.Util;
#if USE_NUGET_V2_LIBS
using NuGet;
using Calamari.NuGet.Versioning;
using VersionComparer = NuGet.Versioning.VersionComparer;
using SemanticVersion = NuGet.Versioning.SemanticVersion;
#else
using NuGet.Packaging;
using NuGet.Versioning;
#endif


namespace Calamari.Integration.Packages.Download
{
    class PackageDownloader
    {
        const string WhyAmINotAllowedToUseDependencies = "http://octopusdeploy.com/documentation/packaging";
        readonly CalamariPhysicalFileSystem fileSystem = CalamariPhysicalFileSystem.GetPhysicalFileSystem();
        public static string RootDirectory => Path.Combine(TentacleHome, "Files");
        public static readonly string DownloadingExtension = ".downloading";

        private static string TentacleHome
        {
            get
            {
                var tentacleHome = Environment.GetEnvironmentVariable("TentacleHome");
                if (tentacleHome == null)
                {
                    Log.Error("Environment variable 'TentacleHome' has not been set.");
                }
                return tentacleHome;
            }
        }

        public void DownloadPackage(string packageId, NuGetVersion version, string feedId, Uri feedUri, ICredentials feedCredentials, bool forcePackageDownload, int maxDownloadAttempts, TimeSpan downloadAttemptBackoff, out string downloadedTo, out string hash, out long size)
        {
            var cacheDirectory = GetPackageRoot(feedId);
            
            LocalNuGetPackage downloaded = null;
            downloadedTo = null;
            if (!forcePackageDownload)
            {
                AttemptToGetPackageFromCache(packageId, version, cacheDirectory, out downloaded, out downloadedTo);
            }

            if (downloaded == null)
            {
                DownloadPackage(packageId, version, feedUri, feedCredentials, cacheDirectory, maxDownloadAttempts, downloadAttemptBackoff, out downloaded, out downloadedTo);
            }
            else
            {
                Log.VerboseFormat("Package was found in cache. No need to download. Using file: '{0}'", downloadedTo);
            }

            size = fileSystem.GetFileSize(downloadedTo);
            string packageHash = null;
            downloaded.GetStream(stream=>  packageHash = HashCalculator.Hash(stream));
            hash = packageHash;
        }

        private void AttemptToGetPackageFromCache(string packageId, NuGetVersion version, string cacheDirectory, out LocalNuGetPackage downloaded, out string downloadedTo)
        {
            downloaded = null;
            downloadedTo = null;

            Log.VerboseFormat("Checking package cache for package {0} {1}", packageId, version.ToString());

            var name = GetNameOfPackage(packageId, version.ToString());
            fileSystem.EnsureDirectoryExists(cacheDirectory);
            
            var files = fileSystem.EnumerateFilesRecursively(cacheDirectory, name + "*.nupkg");

            foreach (var file in files)
            {
                var package = ReadPackageFile(file);
                if (package == null)
                    continue;

               
                var idMatches = string.Equals(package.Metadata.Id, packageId, StringComparison.OrdinalIgnoreCase);
                var versionExactMatch = string.Equals(package.Metadata.Version.ToString(), version.ToString(), StringComparison.OrdinalIgnoreCase);
#if USE_NUGET_V2_LIBS
                var nugetVerMatches = NuGetVersion.TryParse(package.Metadata.Version, out NuGetVersion packageVersion) &&
                        VersionComparer.Default.Equals(version, packageVersion);
#else
                var nugetVerMatches = package.Metadata.Version.Equals(version);
#endif

                if (idMatches && (nugetVerMatches || versionExactMatch))
                {
                    downloaded = package;
                    downloadedTo = file;
                    return;
                }
            }
        }

        private LocalNuGetPackage ReadPackageFile(string filePath)
        {
            try
            {
                return new LocalNuGetPackage(filePath);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (FileFormatException)
            {
                return null;
            }
        }

        private string GetPackageRoot(string prefix)
        {
            return string.IsNullOrWhiteSpace(prefix) ? RootDirectory : Path.Combine(RootDirectory, prefix);
        }

        private string GetNameOfPackage(string packageId, string version)
        {
            return $"{packageId}.{version}_";
        }

        private void DownloadPackage(string packageId, NuGetVersion version, Uri feedUri, ICredentials feedCredentials, string cacheDirectory, int maxDownloadAttempts, TimeSpan downloadAttemptBackoff, out LocalNuGetPackage downloaded, out string downloadedTo)
        {
            Log.Info("Downloading NuGet package {0} {1} from feed: '{2}'", packageId, version, feedUri);
            Log.VerboseFormat("Downloaded package will be stored in: '{0}'", cacheDirectory);
            fileSystem.EnsureDirectoryExists(cacheDirectory);
            fileSystem.EnsureDiskHasEnoughFreeSpace(cacheDirectory);

            var fullPathToDownloadTo = GetFilePathToDownloadPackageTo(cacheDirectory, packageId, version.ToString());

            var downloader = new NuGetPackageDownloader(fileSystem);
            downloader.DownloadPackage(packageId, version, feedUri, feedCredentials, fullPathToDownloadTo, maxDownloadAttempts, downloadAttemptBackoff); 

            downloaded = new LocalNuGetPackage(fullPathToDownloadTo);
            downloadedTo = fullPathToDownloadTo; 
            CheckWhetherThePackageHasDependencies(downloaded.Metadata);
        }


        string GetFilePathToDownloadPackageTo(string cacheDirectory, string packageId, string version)
        {
            var name = packageId + "." + version + "_" + BitConverter.ToString(Guid.NewGuid().ToByteArray()).Replace("-", string.Empty) + ".nupkg";
            return Path.Combine(cacheDirectory, name);
        }

        void CheckWhetherThePackageHasDependencies(ManifestMetadata downloaded)
        {
#if USE_NUGET_V3_LIBS
            var dependencies = downloaded.DependencyGroups.SelectMany(ds => ds.Packages).ToArray();
#else
            var dependencies = downloaded.DependencySets.SelectMany(ds => ds.Dependencies).ToArray();
#endif
            if (dependencies.Any())
            {
                Log.Info("NuGet packages with dependencies are not currently supported, and dependencies won't be installed on the Tentacle. The package '{0} {1}' appears to have the following dependencies: {2}. For more information please see {3}",
                               downloaded.Id,
                               downloaded.Version,
                               string.Join(", ", dependencies.Select(dependency => dependency.ToString())),
                               WhyAmINotAllowedToUseDependencies);
            }
        }
    }
}