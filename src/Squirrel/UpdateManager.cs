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
// Modified On:  2020/03/21 21:25
// Modified By:  Alexis

#endregion




using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using NuGet;
using Splat;
using Squirrel.Extensions;
using Squirrel.Shell;

namespace Squirrel
{
  public sealed partial class UpdateManager : IUpdateManager, IEnableLogger
  {
    #region Constants & Statics

    private static bool _exiting = false;

    #endregion




    #region Properties & Fields - Non-Public

    private readonly string          _rootAppDirectory;
    private readonly string          _applicationName;
    private readonly IFileDownloader _urlDownloader;
    private readonly string          _updateUrlOrPath;

    private IDisposable _updateLock;

    #endregion




    #region Constructors

    public UpdateManager(string          urlOrPath,
                         string          applicationName = null,
                         string          rootDirectory   = null,
                         IFileDownloader urlDownloader   = null)
    {
      Contract.Requires(!String.IsNullOrEmpty(urlOrPath));
      Contract.Requires(!String.IsNullOrEmpty(applicationName));

      _updateUrlOrPath = urlOrPath;
      _applicationName = applicationName ?? getApplicationName();
      _urlDownloader   = urlDownloader ?? new FileDownloader();

      if (rootDirectory != null)
      {
        _rootAppDirectory = Path.Combine(rootDirectory, _applicationName);
        return;
      }

      _rootAppDirectory = Path.Combine(GetLocalAppDataDirectory(), _applicationName);
    }

    ~UpdateManager()
    {
      if (_updateLock != null && !_exiting)
        throw new Exception("You must dispose UpdateManager!");
    }

    public void Dispose()
    {
      var disposable = Interlocked.Exchange(ref _updateLock, null);

      disposable?.Dispose();
    }

    #endregion




    #region Properties & Fields - Public

    public string ApplicationName => _applicationName;

    public string RootAppDirectory => _rootAppDirectory;

    #endregion




    #region Properties Impl - Public

    public bool IsInstalledApp => Assembly.GetExecutingAssembly().Location
                                          .StartsWith(RootAppDirectory, StringComparison.OrdinalIgnoreCase);

    #endregion




    #region Methods Impl

    /// <inheritdoc />
    public Task<RemoteAndLocalReleases> FetchAllReleases(
      Action<int>      progress  = null,
      UpdaterIntention intention = UpdaterIntention.Update)
    {
      var checkForUpdate = new CheckForUpdateImpl(_rootAppDirectory);

      return checkForUpdate.ReadAndParseReleasesFile(
        intention,
        Utility.LocalReleaseFileForAppDir(_rootAppDirectory),
        _updateUrlOrPath,
        progress,
        _urlDownloader);
    }

    /// <inheritdoc />
    public UpdateInfo CalculateUpdatePath(
      RemoteAndLocalReleases remoteAndLocalReleases,
      ReleaseEntry           targetRelease,
      bool                   allowDowngrade,
      bool                   ignoreDeltaUpdates = false,
      UpdaterIntention       intention          = UpdaterIntention.Update)
    {
      var checkForUpdate = new CheckForUpdateImpl(_rootAppDirectory);

      return checkForUpdate.CalculateUpdateInfo(
        intention,
        remoteAndLocalReleases,
        targetRelease,
        ignoreDeltaUpdates,
        allowDowngrade);
    }

    /// <inheritdoc />
    public async Task<UpdateInfo> CheckForUpdate(
      bool             allowDowngrade,
      bool             ignoreDeltaUpdates = false,
      Action<int>      progress           = null,
      UpdaterIntention intention          = UpdaterIntention.Update)
    {
      var checkForUpdate = new CheckForUpdateImpl(_rootAppDirectory);

      using (await acquireUpdateLock().ConfigureAwait(false))
        return await checkForUpdate.CheckForUpdate(
                                     intention,
                                     Utility.LocalReleaseFileForAppDir(_rootAppDirectory),
                                     _updateUrlOrPath,
                                     allowDowngrade,
                                     ignoreDeltaUpdates,
                                     progress,
                                     _urlDownloader)
                                   .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task DownloadRelease(
      ReleaseEntry releaseToDownload,
      Action<int>  progress = null)
    {
      return DownloadReleases(new[] { releaseToDownload }, progress);
    }

    /// <inheritdoc />
    public async Task DownloadReleases(
      IEnumerable<ReleaseEntry> releasesToDownload,
      Action<int>               progress = null)
    {
      var downloadReleases = new DownloadReleasesImpl(_rootAppDirectory);

      using (await acquireUpdateLock().ConfigureAwait(false))
        await downloadReleases.DownloadReleases(_updateUrlOrPath, releasesToDownload, progress, _urlDownloader)
                              .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> ApplyReleases(
      UpdateInfo  updateInfo,
      Action<int> progress = null)
    {
      var applyReleases = new ApplyReleasesImpl(_rootAppDirectory);

      using (await acquireUpdateLock().ConfigureAwait(false))
        return await applyReleases.ApplyReleases(updateInfo, false, false, progress)
                                  .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task FullInstall(
      bool        silentInstall = false,
      Action<int> progress      = null)
    {
      var updateInfo = await CheckForUpdate(false, intention: UpdaterIntention.Install)
        .ConfigureAwait(false);
      await DownloadReleases(updateInfo.ReleasesToApply)
        .ConfigureAwait(false);

      var applyReleases = new ApplyReleasesImpl(_rootAppDirectory);

      using (await acquireUpdateLock().ConfigureAwait(false))
        await applyReleases.ApplyReleases(updateInfo, silentInstall, true, progress)
                           .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task FullUninstall()
    {
      var applyReleases = new ApplyReleasesImpl(_rootAppDirectory);

      using (await acquireUpdateLock())
      {
        KillAllExecutablesBelongingToPackage();
        await applyReleases.FullUninstall();
      }
    }

    /// <inheritdoc />
    public Task<RegistryKey> CreateUninstallerRegistryEntry(
      string uninstallCmd,
      string quietSwitch)
    {
      var installHelpers = new InstallHelperImpl(_applicationName, _rootAppDirectory);
      return installHelpers.CreateUninstallerRegistryEntry(uninstallCmd, quietSwitch);
    }

    /// <inheritdoc />
    public Task<RegistryKey> CreateUninstallerRegistryEntry()
    {
      var installHelpers = new InstallHelperImpl(_applicationName, _rootAppDirectory);
      return installHelpers.CreateUninstallerRegistryEntry();
    }

    /// <inheritdoc />
    public void RemoveUninstallerRegistryEntry()
    {
      var installHelpers = new InstallHelperImpl(_applicationName, _rootAppDirectory);
      installHelpers.RemoveUninstallerRegistryEntry();
    }

    /// <inheritdoc />
    public void CreateShortcutsForExecutable(
      string           exeName,
      ShortcutLocation locations,
      bool             updateOnly,
      string           programArguments = null,
      string           icon             = null)
    {
      var installHelpers = new ApplyReleasesImpl(_rootAppDirectory);
      installHelpers.CreateShortcutsForExecutable(exeName, locations, updateOnly, programArguments, icon);
    }

    /// <inheritdoc />
    public void RemoveShortcutsForExecutable(
      string           exeName,
      ShortcutLocation locations)
    {
      var installHelpers = new ApplyReleasesImpl(_rootAppDirectory);
      installHelpers.RemoveShortcutsForExecutable(exeName, locations);
    }

    /// <inheritdoc />
    public SemanticVersion CurrentlyInstalledVersion(string executable = null)
    {
      executable = executable ??
        Path.GetDirectoryName(typeof(UpdateManager).Assembly.Location);

      if (!executable.StartsWith(_rootAppDirectory, StringComparison.OrdinalIgnoreCase))
        return null;

      var appDirName = executable.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                 .FirstOrDefault(x => x.StartsWith("app-", StringComparison.OrdinalIgnoreCase));

      if (appDirName == null) return null;

      return appDirName.ToSemanticVersion();
    }

    #endregion




    #region Methods

    public Dictionary<ShortcutLocation, ShellLink> GetShortcutsForExecutable(
      string           exeName,
      ShortcutLocation locations,
      string           programArguments = null)
    {
      var installHelpers = new ApplyReleasesImpl(_rootAppDirectory);
      return installHelpers.GetShortcutsForExecutable(exeName, locations, programArguments);
    }

    public void KillAllExecutablesBelongingToPackage()
    {
      var installHelpers = new InstallHelperImpl(_applicationName, _rootAppDirectory);
      installHelpers.KillAllProcessesBelongingToPackage();
    }

    public static void RestartApp(string exeToStart = null, string arguments = null)
    {
      // NB: Here's how this method works:
      //
      // 1. We're going to pass the *name* of our EXE and the params to 
      //    Update.exe
      // 2. Update.exe is going to grab our PID (via getting its parent), 
      //    then wait for us to exit.
      // 3. We exit cleanly, dropping any single-instance mutexes or 
      //    whatever.
      // 4. Update.exe unblocks, then we launch the app again, possibly 
      //    launching a different version than we started with (this is why
      //    we take the app's *name* rather than a full path)

      exeToStart = exeToStart ?? Path.GetFileName(Assembly.GetEntryAssembly().Location);
      var argsArg = arguments != null ? String.Format("-a \"{0}\"", arguments) : "";

      _exiting = true;

      Process.Start(getUpdateExe(), String.Format("--processStartAndWait {0} {1}", exeToStart, argsArg));

      // NB: We have to give update.exe some time to grab our PID, but
      // we can't use WaitForInputIdle because we probably don't have
      // whatever WaitForInputIdle considers a message loop.
      Thread.Sleep(500);
      Environment.Exit(0);
    }

    public static async Task<Process> RestartAppWhenExited(string exeToStart = null, string arguments = null)
    {
      // NB: Here's how this method works:
      //
      // 1. We're going to pass the *name* of our EXE and the params to 
      //    Update.exe
      // 2. Update.exe is going to grab our PID (via getting its parent), 
      //    then wait for us to exit.
      // 3. Return control and new Process back to caller and allow them to Exit as desired.
      // 4. After our process exits, Update.exe unblocks, then we launch the app again, possibly 
      //    launching a different version than we started with (this is why
      //    we take the app's *name* rather than a full path)

      exeToStart = exeToStart ?? Path.GetFileName(Assembly.GetEntryAssembly().Location);
      var argsArg = arguments != null ? String.Format("-a \"{0}\"", arguments) : "";

      _exiting = true;

      var updateProcess = Process.Start(getUpdateExe(), String.Format("--processStartAndWait {0} {1}", exeToStart, argsArg));

      await Task.Delay(500);

      return updateProcess;
    }

    public static string GetLocalAppDataDirectory(string assemblyLocation = null)
    {
      // Try to divine our our own install location via reading tea leaves
      //
      // * We're Update.exe, running in the app's install folder
      // * We're Update.exe, running on initial install from SquirrelTemp
      // * We're a C# EXE with Squirrel linked in

      var assembly = Assembly.GetEntryAssembly();
      if (assemblyLocation == null && assembly == null) // dunno lol
        return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

      assemblyLocation = assemblyLocation ?? assembly.Location;

      if (Path.GetFileName(assemblyLocation).Equals("update.exe", StringComparison.OrdinalIgnoreCase))
      {
        // NB: Both the "SquirrelTemp" case and the "App's folder" case 
        // mean that the root app dir is one up
        var oneFolderUpFromAppFolder = Path.Combine(Path.GetDirectoryName(assemblyLocation), "..");
        return Path.GetFullPath(oneFolderUpFromAppFolder);
      }

      var twoFoldersUpFromAppFolder = Path.Combine(Path.GetDirectoryName(assemblyLocation), "..\\..");
      return Path.GetFullPath(twoFoldersUpFromAppFolder);
    }

    private Task<IDisposable> acquireUpdateLock()
    {
      if (_updateLock != null) return Task.FromResult(_updateLock);

      return Task.Run(() =>
      {
        var key = Utility.CalculateStreamSHA1(new MemoryStream(Encoding.UTF8.GetBytes(_rootAppDirectory)));

        IDisposable theLock;
        try
        {
          theLock = ModeDetector.InUnitTestRunner()
            ? Disposable.Create(() => { })
            : new SingleGlobalInstance(key, TimeSpan.FromMilliseconds(2000));
        }
        catch (TimeoutException)
        {
          throw new TimeoutException("Couldn't acquire update lock, another instance may be running updates");
        }

        var ret = Disposable.Create(() =>
        {
          theLock.Dispose();
          _updateLock = null;
        });

        _updateLock = ret;
        return ret;
      });
    }

    private static string getApplicationName()
    {
      var fi = new FileInfo(getUpdateExe());
      return fi.Directory.Name;
    }

    private static string getUpdateExe()
    {
      var assembly = Assembly.GetEntryAssembly();

      // Are we update.exe?
      if (assembly != null &&
        Path.GetFileName(assembly.Location).Equals("update.exe", StringComparison.OrdinalIgnoreCase) &&
        assembly.Location.IndexOf("app-", StringComparison.OrdinalIgnoreCase) == -1 &&
        assembly.Location.IndexOf("SquirrelTemp", StringComparison.OrdinalIgnoreCase) == -1)
        return Path.GetFullPath(assembly.Location);

      assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

      var updateDotExe = Path.Combine(Path.GetDirectoryName(assembly.Location), "..\\Update.exe");
      var target       = new FileInfo(updateDotExe);

      if (!target.Exists) throw new Exception("Update.exe not found, not a Squirrel-installed app?");

      return target.FullName;
    }

    #endregion
  }
}
