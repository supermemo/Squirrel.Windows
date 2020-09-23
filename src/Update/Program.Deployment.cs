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
// Created On:   2020/09/13 10:12
// Modified On:  2020/09/13 10:12
// Modified By:  Alexis

#endregion




namespace Squirrel.Update
{
  using System;
  using System.Diagnostics;
  using System.IO;
  using System.Linq;
  using System.Reflection;
  using System.Text;
  using System.Threading.Tasks;
  using Json;
  using Splat;
  using System.Windows;
  using global::Update;
  using global::Update.UI;
  using NuGet;

  /// <summary>
  /// The program.
  /// </summary>
  internal partial class Program
  {
    #region Methods

    /// <summary>
    /// The install.
    /// </summary>
    /// <param name="silentInstall">
    /// The silent install.
    /// </param>
    /// <param name="progressSource">
    /// The progress source.
    /// </param>
    /// <param name="sourceDirectory">
    /// The source directory.
    /// </param>
    /// <returns>
    /// The <see cref="Task"/>.
    /// </returns>
    public async Task Install(bool silentInstall, ProgressSource progressSource, string sourceDirectory = null)
    {
      sourceDirectory = sourceDirectory ?? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
      var releasesPath = Path.Combine(sourceDirectory, "RELEASES");

      this.Log().Info("Starting install, writing to {0}", sourceDirectory);

      if (!File.Exists(releasesPath))
      {
        this.Log().Info("RELEASES doesn't exist, creating it at " + releasesPath);
        var nupkgs = new DirectoryInfo(sourceDirectory).GetFiles()
                                                       .Where(x => x.Name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                                                       .Select(x => ReleaseEntry.GenerateFromFile(x.FullName));

        ReleaseEntry.WriteReleaseFile(nupkgs, releasesPath);
      }

      var ourAppName = ReleaseEntry.ParseReleaseFile(File.ReadAllText(releasesPath, Encoding.UTF8))
                                   .First().PackageName;

      using (var mgr = new UpdateManager(sourceDirectory, ourAppName))
      {
        this.Log().Info("About to install to: " + mgr.RootAppDirectory);

        if (Directory.Exists(mgr.RootAppDirectory))
        {
          this.Log().Warn("Install path {0} already exists, burning it to the ground", mgr.RootAppDirectory);

          mgr.KillAllExecutablesBelongingToPackage();
          await Task.Delay(500);

          await this.ErrorIfThrows(() => Utility.DeleteDirectory(mgr.RootAppDirectory, false),
                                   "Failed to remove existing directory on full install, is the app still running???");

          this.ErrorIfThrows(() => Utility.Retry(() => Directory.CreateDirectory(mgr.RootAppDirectory), 3),
                             "Couldn't recreate app directory, perhaps Antivirus is blocking it");
        }

        Directory.CreateDirectory(mgr.RootAppDirectory);

        var updateTarget = Path.Combine(mgr.RootAppDirectory, "Update.exe");
        this.ErrorIfThrows(() => Utility.Retry(() => File.Copy(Assembly.GetExecutingAssembly().Location, updateTarget, true), 3),
                           "Failed to copy Update.exe to " + updateTarget);

        await mgr.FullInstall(silentInstall, progressSource.Raise);

        await this.ErrorIfThrows(() => mgr.CreateUninstallerRegistryEntry(),
                                 "Failed to create uninstaller registry entry");
      }
    }

    /// <summary>
    /// The uninstall.
    /// </summary>
    /// <param name="appName">
    /// The app name.
    /// </param>
    /// <returns>
    /// The <see cref="Task"/>.
    /// </returns>
    public async Task Uninstall(string appName = null)
    {
      this.Log().Info("Starting uninstall for app: " + appName);

      appName = appName ?? getAppNameFromDirectory();
      using (var mgr = new UpdateManager(string.Empty, appName))
      {
        await mgr.FullUninstall();
        mgr.RemoveUninstallerRegistryEntry();
      }
    }
    /// <summary>
    /// The show updater.
    /// </summary>
    /// <returns>
    /// The <see cref="int"/>.
    /// </returns>
    public int ShowUpdater()
    {
      var urlOrPath = string.IsNullOrWhiteSpace(opt.updateUrl)
        ? Resources.BaseUrl
        : opt.updateUrl;

      if (string.IsNullOrWhiteSpace(urlOrPath))
      {
        ShowHelp();
        Console.WriteLine($"Resources: '{Resources.AppTitle}' - '{Resources.PackageName}' - '{Resources.BaseUrl}'");
        Console.WriteLine($"UrlOrPath: '{urlOrPath}'");
        return -1;
      }

      var application = new Application
      {
        ShutdownMode = ShutdownMode.OnLastWindowClose, MainWindow = new UpdateWindow(urlOrPath)
      };

      application.MainWindow.ShowDialog();

      return 0;
    }

    /// <summary>
    /// The update.
    /// </summary>
    /// <param name="updateUrl">
    /// The update url.
    /// </param>
    /// <param name="appName">
    /// The app name.
    /// </param>
    /// <returns>
    /// The <see cref="Task"/>.
    /// </returns>
    public async Task Update(string updateUrl, string appName = null)
    {
      appName = appName ?? getAppNameFromDirectory();

      this.Log().Info("Starting update, downloading from " + updateUrl);

      using (var mgr = new UpdateManager(updateUrl, appName))
      {
        bool ignoreDeltaUpdates = false;
        this.Log().Info("About to update to: " + mgr.RootAppDirectory);

        retry:
        try
        {
          var updateInfo = await mgr.CheckForUpdate(intention: UpdaterIntention.Update,
                                                    allowDowngrade: true,
                                                    ignoreDeltaUpdates: ignoreDeltaUpdates,
                                                    progress: x => Console.WriteLine(x / 3));
          await mgr.DownloadReleases(updateInfo.ReleasesToApply, x => Console.WriteLine(33 + x / 3));
          await mgr.ApplyReleases(updateInfo, x => Console.WriteLine(66 + x / 3));
        }
        catch (Exception ex)
        {
          if (ignoreDeltaUpdates)
          {
            this.Log().ErrorException("Really couldn't apply updates!", ex);
            throw;
          }

          this.Log().WarnException("Failed to apply updates, falling back to full updates", ex);
          ignoreDeltaUpdates = true;
          goto retry;
        }

        var updateTarget = Path.Combine(mgr.RootAppDirectory, "Update.exe");

        await this.ErrorIfThrows(() =>
                                   mgr.CreateUninstallerRegistryEntry(),
                                 "Failed to create uninstaller registry entry");
      }
    }

    /// <summary>
    /// The update self.
    /// </summary>
    /// <returns>
    /// The <see cref="Task"/>.
    /// </returns>
    public async Task UpdateSelf()
    {
      waitForParentToExit();
      var src = Assembly.GetExecutingAssembly().Location;
      var updateDotExeForOurPackage = Path.Combine(
        Path.GetDirectoryName(src),
        "..", "Update.exe");

      await Task.Run(() => { File.Copy(src, updateDotExeForOurPackage, true); });
    }

    /// <summary>
    /// The check for update.
    /// </summary>
    /// <param name="updateUrl">
    /// The update url.
    /// </param>
    /// <param name="appName">
    /// The app name.
    /// </param>
    /// <returns>
    /// The <see cref="Task"/>.
    /// </returns>
    public async Task<string> CheckForUpdate(string updateUrl, string appName = null)
    {
      appName = appName ?? getAppNameFromDirectory();

      this.Log().Info("Fetching update information, downloading from " + updateUrl);
      using (var mgr = new UpdateManager(updateUrl, appName))
      {
        var updateInfo = await mgr.CheckForUpdate(
          intention: UpdaterIntention.Update,
          allowDowngrade: true,
          progress: x => Console.WriteLine(x));
        var releaseNotes = updateInfo.FetchReleaseNotes();

        var sanitizedUpdateInfo = new
        {
          currentVersion = updateInfo.CurrentlyInstalledVersion.Version.ToString(),
          futureVersion  = updateInfo.FutureReleaseEntry.Version.ToString(),
          releasesToApply = updateInfo.ReleasesToApply.Select(x => new
          {
            version      = x.Version.ToString(),
            releaseNotes = releaseNotes.ContainsKey(x) ? releaseNotes[x] : string.Empty,
          }).ToArray(),
        };

        return SimpleJson.SerializeObject(sanitizedUpdateInfo);
      }
    }

    public async Task<string> Download(string updateUrl, string appName = null)
    {
      appName = appName ?? getAppNameFromDirectory();

      this.Log().Info("Fetching update information, downloading from " + updateUrl);

      using (var mgr = new UpdateManager(updateUrl, appName))
      {
        var updateInfo = await mgr.CheckForUpdate(
          intention: UpdaterIntention.Update,
          allowDowngrade: true,
          progress: x => Console.WriteLine(x / 3));
        await mgr.DownloadReleases(updateInfo.ReleasesToApply, x => Console.WriteLine(33 + x / 3));

        var releaseNotes = updateInfo.FetchReleaseNotes();

        var sanitizedUpdateInfo = new
        {
          currentVersion = updateInfo.CurrentlyInstalledVersion.Version.ToString(),
          futureVersion  = updateInfo.FutureReleaseEntry.Version.ToString(),
          releasesToApply = updateInfo.ReleasesToApply.Select(x => new
          {
            version      = x.Version.ToString(),
            releaseNotes = releaseNotes.ContainsKey(x) ? releaseNotes[x] : "",
          }).ToArray(),
        };

        return SimpleJson.SerializeObject(sanitizedUpdateInfo);
      }
    }

    public void Shortcut(string exeName, string shortcutArgs, string processStartArgs, string icon)
    {
      if (String.IsNullOrWhiteSpace(exeName))
      {
        ShowHelp();
        return;
      }

      var appName          = getAppNameFromDirectory();
      var defaultLocations = ShortcutLocation.StartMenu | ShortcutLocation.Desktop;
      var locations        = parseShortcutLocations(shortcutArgs);

      using (var mgr = new UpdateManager("", appName))
        mgr.CreateShortcutsForExecutable(exeName, locations ?? defaultLocations, false, processStartArgs, icon);
    }

    public void Deshortcut(string exeName, string shortcutArgs)
    {
      if (String.IsNullOrWhiteSpace(exeName))
      {
        ShowHelp();
        return;
      }

      var appName          = getAppNameFromDirectory();
      var defaultLocations = ShortcutLocation.StartMenu | ShortcutLocation.Desktop;
      var locations        = parseShortcutLocations(shortcutArgs);

      using (var mgr = new UpdateManager("", appName))
        mgr.RemoveShortcutsForExecutable(exeName, locations ?? defaultLocations);
    }

    public void ProcessStart(string exeName, string arguments, bool shouldWait)
    {
      if (String.IsNullOrWhiteSpace(exeName))
      {
        ShowHelp();
        return;
      }

      // Find the latest installed version's app dir
      var appDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
      var releases = ReleaseEntry.ParseReleaseFile(
        File.ReadAllText(Utility.LocalReleaseFileForAppDir(appDir), Encoding.UTF8));

      // NB: We add the hacked up version in here to handle a migration
      // issue, where versions of Squirrel pre PR #450 will not understand
      // prerelease tags, so it will end up writing the release name sans
      // tags. However, the RELEASES file _will_ have them, so we need to look
      // for directories that match both the real version, and the sanitized
      // version, giving priority to the former.
      var latestAppDir = releases
                         .OrderByDescending(x => x.Version)
                         .SelectMany(x => new[]
                         {
                           Utility.AppDirForRelease(appDir, x),
                           Utility.AppDirForVersion(
                             appDir, new SemanticVersion(x.Version.Version.Major, x.Version.Version.Minor, x.Version.Version.Build, ""))
                         })
                         .FirstOrDefault(x => Directory.Exists(x));

      // Check for the EXE name they want
      var targetExe = new FileInfo(Path.Combine(latestAppDir, exeName.Replace("%20", " ")));
      this.Log().Info("Want to launch '{0}'", targetExe);

      // Check for path canonicalization attacks
      if (!targetExe.FullName.StartsWith(latestAppDir, StringComparison.Ordinal))
        throw new ArgumentException();

      if (!targetExe.Exists)
      {
        this.Log().Error("File {0} doesn't exist in current release", targetExe);
        throw new ArgumentException();
      }

      if (shouldWait) waitForParentToExit();

      try
      {
        this.Log().Info("About to launch: '{0}': {1}", targetExe.FullName, arguments ?? "");
        Process.Start(new ProcessStartInfo(targetExe.FullName, arguments ?? "")
                        { WorkingDirectory = Path.GetDirectoryName(targetExe.FullName) });
      }
      catch (Exception ex)
      {
        this.Log().ErrorException("Failed to start process", ex);
      }
    }

    #endregion
  }
}
