#region License & Metadata

// The MIT License (MIT)
// 
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
// 
// 
// Modified On:  2020/03/20 15:00
// Modified By:  Alexis

#endregion




using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using NuGet;
using Splat;
using Squirrel.Extensions;

namespace Squirrel
{
  public sealed partial class UpdateManager
  {
    internal class CheckForUpdateImpl : IEnableLogger
    {
      #region Properties & Fields - Non-Public

      private readonly string rootAppDirectory;

      #endregion




      #region Constructors

      public CheckForUpdateImpl(string rootAppDirectory)
      {
        this.rootAppDirectory = rootAppDirectory;
      }

      #endregion




      #region Methods

      /// <summary>Fetches the remote RELEASES file, whether it's a local dir or an HTTP URL</summary>
      /// <param name="updateUrlOrPath">Path to the release file. Both file and HTTP uri are accepted</param>
      /// <param name="latestLocalRelease">
      ///   The latest local release, used to send additional metadata to
      ///   the web service if <paramref name="updateUrlOrPath" /> is a web url
      /// </param>
      /// <param name="urlDownloader">The downloader class</param>
      /// <returns>The RELEASES file's content, or <see langword="null" /></returns>
      public async Task<string> ReadRemoteReleasesFile(
        string          updateUrlOrPath,
        ReleaseEntry    latestLocalRelease,
        IFileDownloader urlDownloader = null)
      {
        string releaseFile = null;

        if (Utility.IsHttpUrl(updateUrlOrPath))
        {
          if (updateUrlOrPath.EndsWith("/"))
            updateUrlOrPath = updateUrlOrPath.Substring(0, updateUrlOrPath.Length - 1);

          this.Log().Info("Downloading RELEASES file from {0}", updateUrlOrPath);

          int retries = 3;
          Uri uri     = null;

          while (releaseFile == null)
            try
            {
              uri = Utility.AppendPathToUri(new Uri(updateUrlOrPath), "RELEASES");

              if (latestLocalRelease != null)
                uri = Utility.AddQueryParamsToUri(uri, new Dictionary<string, string>
                {
                  { "id", latestLocalRelease.PackageName },
                  { "localVersion", latestLocalRelease.Version.ToString() },
                  { "arch", Environment.Is64BitOperatingSystem ? "amd64" : "x86" }
                });

              var data = await urlDownloader.DownloadUrl(uri.ToString());
              releaseFile = Encoding.UTF8.GetString(data);
            }
            catch (WebException ex)
            {
              this.Log().InfoException($"Download resulted in WebException while fetching '{uri}' (returning blank release list)", ex);

              if (retries-- == 0)
                throw;
            }
        }

        else
        {
          this.Log().Info("Reading RELEASES file from {0}", updateUrlOrPath);

          if (!Directory.Exists(updateUrlOrPath))
          {
            var message = $"The directory {updateUrlOrPath} does not exist, something is probably broken with your application";

            throw new Exception(message);
          }

          var fi = new FileInfo(Path.Combine(updateUrlOrPath, "RELEASES"));
          if (!fi.Exists)
          {
            var message = $"The file {fi.FullName} does not exist, something is probably broken with your application";

            this.Log().Warn(message);

            var packages = new DirectoryInfo(updateUrlOrPath).GetFiles("*.nupkg");
            if (packages.Length == 0)
              throw new Exception(message);

            // NB: Create a new RELEASES file since we've got a directory of packages
            ReleaseEntry.WriteReleaseFile(
              packages.Select(x => ReleaseEntry.GenerateFromFile(x.FullName)), fi.FullName);
          }

          releaseFile = File.ReadAllText(fi.FullName, Encoding.UTF8);
        }

        return releaseFile;
      }

      /// <summary>Parses the releases entries from the local RELEASES file</summary>
      /// <param name="intention">
      ///   Indicates whether the UpdateManager is used in a Install or Update
      ///   scenario.
      /// </param>
      /// <param name="localReleaseFile">The path to the local RELEASES file</param>
      /// <returns>
      ///   The releases entries contained in the RELEASES file or <see langword="null" />
      /// </returns>
      public async Task<ICollection<ReleaseEntry>> ReadAndParseLocalReleasesFile(
        UpdaterIntention intention,
        string           localReleaseFile)
      {
        var shouldInitialize = intention == UpdaterIntention.Install;

        if (intention != UpdaterIntention.Install)
          try
          {
            return Utility.LoadLocalReleases(localReleaseFile);
          }
          catch (Exception ex)
          {
            // Something has gone pear-shaped, let's start from scratch
            this.Log().WarnException("Failed to load local releases, starting from scratch", ex);
            shouldInitialize = true;
          }

        //
        // Starting from scratch
        if (shouldInitialize)
          await InitializeClientAppDirectory();

        return null;
      }

      /// <summary>Fetches the remote RELEASES file, whether it's a local dir or an HTTP URL</summary>
      /// <param name="updateUrlOrPath">Path to the release file. Both file and HTTP uri are accepted</param>
      /// <param name="intention">
      ///   Indicates whether the UpdateManager is used in a Install or Update
      ///   scenario.
      /// </param>
      /// <param name="localReleaseFile"></param>
      /// <param name="progress"></param>
      /// <param name="urlDownloader">The downloader class</param>
      /// <returns>The remote and local releases, or <see langword="null" /></returns>
      public async Task<RemoteAndLocalReleases> ReadAndParseReleasesFile(
        UpdaterIntention intention,
        string           localReleaseFile,
        string           updateUrlOrPath,
        Action<int>      progress      = null,
        IFileDownloader  urlDownloader = null)
      {
        progress = progress ?? (_ => { });

        //
        // Check local releases
        var localReleases = await ReadAndParseLocalReleasesFile(intention, localReleaseFile);
        var stagingId     = intention == UpdaterIntention.Install ? null : GetOrCreateStagedUserId();

        //
        // Fetch the remote RELEASES file, whether it's a local dir or an HTTP URL
        var    latestLocalRelease = localReleases?.MaxBy(x => x.Version).First();
        string releaseFile        = await ReadRemoteReleasesFile(updateUrlOrPath, latestLocalRelease, urlDownloader);

        progress(50);

        //
        // Parse the RELEASES file
        var remoteReleases = ReleaseEntry.ParseReleaseFileAndApplyStaging(releaseFile, stagingId);

        progress(100);

        return new RemoteAndLocalReleases(remoteReleases, localReleases, latestLocalRelease);
      }

      /// <summary>
      ///   Assesses the releases packages that needs be downloaded to update to the latest
      ///   version
      /// </summary>
      /// <param name="intention">
      ///   Indicates whether the UpdateManager is used in a Install or Update
      ///   scenario.
      /// </param>
      /// <param name="localReleaseFile"></param>
      /// <param name="updateUrlOrPath"></param>
      /// <param name="allowDowngrade"></param>
      /// <param name="ignoreDeltaUpdates"></param>
      /// <param name="progress"></param>
      /// <param name="urlDownloader"></param>
      /// <param name="minPrereleaseString"></param>
      /// <returns></returns>
      public async Task<UpdateInfo> CheckForUpdate(
        UpdaterIntention intention,
        string           localReleaseFile,
        string           updateUrlOrPath,
        bool             allowDowngrade,
        bool             ignoreDeltaUpdates   = false,
        Action<int>      progress             = null,
        IFileDownloader  urlDownloader        = null,
        string           minPrereleaseString  = null)
      {
        progress = progress ?? (_ => { });

        //
        // Read and parse the RELEASES file
        var remoteAndLocalReleases = await ReadAndParseReleasesFile(
          intention,
          localReleaseFile,
          updateUrlOrPath,
          p => progress((int)(p * .66)),
          urlDownloader);

        //
        // Calculate the update path
        if (remoteAndLocalReleases.Remote == null)
        {
          this.Log().Warn("Release information couldn't be determined due to remote corrupt RELEASES file");
          throw new Exception("Corrupt remote RELEASES file");
        }

        if (remoteAndLocalReleases.Remote.Any() == false)
        {
          this.Log().Warn("Release information couldn't be determined due to remote empty or corrupt RELEASES file");
          throw new Exception("Remote RELEASES File is empty or corrupted");
        }

        var ret = CalculateUpdateInfo(
          intention,
          remoteAndLocalReleases,
          ignoreDeltaUpdates,
          allowDowngrade,
          minPrereleaseString);

        progress(100);

        return ret;
      }

      private async Task InitializeClientAppDirectory()
      {
        // On bootstrap, we won't have any of our directories, create them
        var pkgDir = Path.Combine(rootAppDirectory, "packages");

        if (Directory.Exists(pkgDir))
          await Utility.DeleteDirectory(pkgDir, false);

        Directory.CreateDirectory(pkgDir);
      }

      public UpdateInfo CalculateUpdateInfo(UpdaterIntention       intention,
                                            RemoteAndLocalReleases remoteAndLocalReleases,
                                            bool                   ignoreDeltaUpdates,
                                            bool                   allowDowngrade,
                                            string                 minPrereleaseString)
      {
        bool ShouldConsiderVersion(ReleaseEntry re)
        {
          var semVer = re.Version;
          var preReleaseString = semVer.GetPreReleaseString();

          if (string.IsNullOrWhiteSpace(preReleaseString))
            return true; // Stable releases are considered in all scenarios

          if (string.IsNullOrWhiteSpace(minPrereleaseString))
            return false; // We already know this isn't a stable version

          return string.Compare(preReleaseString, minPrereleaseString, StringComparison.Ordinal) >= 0;
        }

        var remoteReleases = remoteAndLocalReleases.Remote;

        if (remoteReleases == null)
          throw new ArgumentNullException(nameof(remoteReleases));

        //
        // Calculate update path for the latest version

        var consideredVersions = minPrereleaseString == null
          ? remoteReleases.ToList()
          : remoteReleases.Where(ShouldConsiderVersion).ToList();

        var latestFullRelease = consideredVersions.Any()
          ? consideredVersions.MaxBy(r => r.Version).FirstOrDefault()
          : Utility.FindCurrentVersion(remoteAndLocalReleases.Local);

        return CalculateUpdateInfo(
          intention,
          remoteAndLocalReleases,
          latestFullRelease,
          ignoreDeltaUpdates,
          allowDowngrade);
      }

      public UpdateInfo CalculateUpdateInfo(UpdaterIntention       intention,
                                            RemoteAndLocalReleases remoteAndLocalReleases,
                                            ReleaseEntry           targetRelease,
                                            bool                   ignoreDeltaUpdates,
                                            bool                   allowDowngrade)
      {
        if (targetRelease == null)
          throw new ArgumentNullException(nameof(targetRelease));

        if (remoteAndLocalReleases.Remote == null)
          throw new ArgumentNullException(nameof(remoteAndLocalReleases.Remote));

        var remoteReleases = new HashSet<ReleaseEntry>(
          ignoreDeltaUpdates
            ? remoteAndLocalReleases.Remote.Where(x => !x.IsDelta)
            : remoteAndLocalReleases.Remote);

        if (remoteReleases.Any() == false)
          throw new ArgumentException("CalculateUpdateInfo: No valid remote release available");

        var remoteVersions = new HashSet<SemanticVersion>(remoteReleases.Select(r => r.Version));

        //
        // Make sure that the local releases also belong to the remote RELEASES
        ReleaseEntry currentRelease = null;
          
        if (remoteAndLocalReleases.Local?.Any() ?? false)
          currentRelease = Utility.FindCurrentVersion(remoteAndLocalReleases.Local);

        var localVersions = remoteAndLocalReleases.Local == null
          ? new HashSet<SemanticVersion>()
          : new HashSet<SemanticVersion>(remoteAndLocalReleases.Local
                                                               .Where(lr => remoteVersions.Contains(lr.Version))
                                                               .Select(lr => lr.Version));

        if (currentRelease != null && localVersions.Contains(currentRelease.Version) == false)
          currentRelease = null;

        //
        // Check if there are any existing packages to start the update from, or if we are starting fresh

        var packageDirectory = Utility.PackageDirectoryForAppDir(rootAppDirectory);

        if (currentRelease == null)
        {
          if (intention == UpdaterIntention.Install)
            this.Log().Info("First run, starting from scratch");

          else
            this.Log().Warn("No local releases found, starting from scratch");

          return UpdateInfo.Create(null, targetRelease, remoteReleases, packageDirectory, allowDowngrade);
        }

        //
        // Make sure we aren't trying to calculate a path for the identical release

        if (targetRelease.Version.Equals(currentRelease.Version))
        {
          this.Log().Info("No updates, remote and local are the same");

          var info = UpdateInfo.Create(currentRelease, targetRelease, remoteReleases, packageDirectory, allowDowngrade);
          return info;
        }

        //
        // Compute the update path

        return UpdateInfo.Create(currentRelease, targetRelease, remoteReleases, packageDirectory, allowDowngrade);
      }

      internal Guid? GetOrCreateStagedUserId()
      {
        var stagedUserIdFile = Path.Combine(rootAppDirectory, "packages", ".betaId");
        var ret              = default(Guid);

        try
        {
          if (!Guid.TryParse(File.ReadAllText(stagedUserIdFile, Encoding.UTF8), out ret))
            throw new Exception("File was read but contents were invalid");

          this.Log().Info("Using existing staging user ID: {0}", ret.ToString());
          return ret;
        }
        catch (Exception ex)
        {
          this.Log().DebugException("Couldn't read staging user ID, creating a blank one", ex);
        }

        var prng = new Random();
        var buf  = new byte[4096];
        prng.NextBytes(buf);

        ret = Utility.CreateGuidFromHash(buf);
        try
        {
          File.WriteAllText(stagedUserIdFile, ret.ToString(), Encoding.UTF8);
          this.Log().Info("Generated new staging user ID: {0}", ret.ToString());
          return ret;
        }
        catch (Exception ex)
        {
          this.Log().WarnException("Couldn't write out staging user ID, this user probably shouldn't get beta anything", ex);
          return null;
        }
      }

      #endregion
    }
  }
}
