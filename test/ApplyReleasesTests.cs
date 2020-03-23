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
// Modified On:  2020/03/20 16:03
// Modified By:  Alexis

#endregion




using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NuGet;
using Splat;
using Squirrel.Extensions;
using Squirrel.Tests.TestHelpers;
using Xunit;
// ReSharper disable UnusedVariable

namespace Squirrel.Tests
{
  public class FakeUrlDownloader : IFileDownloader
  {
    #region Methods Impl

    public Task<byte[]> DownloadUrl(string url)
    {
      return Task.FromResult(new byte[0]);
    }

    public async Task DownloadFile(string url, string targetFile, Action<int> progress) { }

    #endregion
  }

  public class ApplyReleasesTests : TestBase
  {
    [Fact]
    public async Task ApplyReleasesWithDeltaPackage()
    {
      using (SetupFixture(0, out var appDir, out var packagesDir,
                          out var remoteAndLocalReleases,
                          "Squirrel.Core.1.0.0.0-full.nupkg",
                          "Squirrel.Core.1.1.0.0-delta.nupkg",
                          "Squirrel.Core.1.1.0.0-full.nupkg"))
      using (var updateMgr = new UpdateManager("https://not.available", "theApp", appDir.Parent.FullName))
      {
        var (currentRelease, deltaEntry, latestFullEntry) = (IList<ReleaseEntry>)remoteAndLocalReleases.Remote;

        var updateInfo = updateMgr.CalculateUpdatePath(
          remoteAndLocalReleases,
          latestFullEntry,
          true);
        updateInfo.ReleasesToApply.Contains(deltaEntry).ShouldBeTrue();
        updateInfo.ReleasesToApply.Count.ShouldEqual(1);

        await TestProgress(p => updateMgr.ApplyReleases(updateInfo, p));

        TestFileVersion(Path.Combine(appDir.FullName, "app-1.1.0.0"),
                        ("NLog.dll", new Version("2.0.0.0")),
                        ("NSync.Core.dll", new Version("1.1.0.0")));
      }
    }

    [Fact]
    public async Task ApplyReleasesWithFullPackage()
    {
      using (SetupFixture(0, out var appDir, out var packagesDir,
                          out var remoteAndLocalReleases,
                          "Squirrel.Core.1.0.0.0-full.nupkg",
                          "Squirrel.Core.1.1.0.0-full.nupkg"))
      using (var updateMgr = new UpdateManager("https://not.available", "theApp", appDir.Parent.FullName))
      {
        var (currentRelease, latestFullEntry) = (IList<ReleaseEntry>)remoteAndLocalReleases.Remote;
        
        var updateInfo = updateMgr.CalculateUpdatePath(
          remoteAndLocalReleases,
          latestFullEntry,
          true);
        updateInfo.ReleasesToApply.Contains(latestFullEntry).ShouldBeTrue();
        updateInfo.ReleasesToApply.Count.ShouldEqual(1);

        await TestProgress(p => updateMgr.ApplyReleases(updateInfo, p));
        
        TestFileVersion(Path.Combine(appDir.FullName, "app-1.1.0.0"),
                        ("NLog.dll", new Version("2.0.0.0")),
                        ("NSync.Core.dll", new Version("1.1.0.0")));
      }
    }

      [Fact]
      public async Task ApplyReleasesWithTargetSuperiorToCurrentVersionAndInferiorToLatest()
      {
        using (SetupFixture(0, out var appDir, out var packagesDir,
                            out var remoteAndLocalReleases,
                            "Squirrel.Core.1.0.0.0-full.nupkg",
                            "Squirrel.Core.1.1.0.0-delta.nupkg",
                            "Squirrel.Core.1.1.0.0-full.nupkg",
                            "Squirrel.Core.1.2.0.0-full.nupkg"))
        using (var updateMgr = new UpdateManager("https://not.available", "theApp", appDir.Parent.FullName))
        {
          var (currentRelease, deltaEntry, fullEntry, latestFullEntry) = (IList<ReleaseEntry>)remoteAndLocalReleases.Remote;

          var updateInfo = updateMgr.CalculateUpdatePath(
            remoteAndLocalReleases,
            fullEntry,
            true);
          updateInfo.ReleasesToApply.Contains(deltaEntry).ShouldBeTrue();
          updateInfo.ReleasesToApply.Count.ShouldEqual(1);

          await TestProgress(p => updateMgr.ApplyReleases(updateInfo, p));

          TestFileVersion(Path.Combine(appDir.FullName, "app-1.1.0.0"),
                          ("NLog.dll", new Version("2.0.0.0")),
                          ("NSync.Core.dll", new Version("1.1.0.0")));
        }
      }

      [Fact]
      public async Task ApplyReleasesWithTargetInferiorToCurrentVersionWithFullPkg()
      {
        using (SetupFixture(3, out var appDir, out var packagesDir,
                            out var remoteAndLocalReleases,
                            "Squirrel.Core.1.0.0.0-full.nupkg",
                            "Squirrel.Core.1.1.0.0-delta.nupkg",
                            "Squirrel.Core.1.1.0.0-full.nupkg",
                            "Squirrel.Core.1.2.0.0-full.nupkg"))
        using (var updateMgr = new UpdateManager("https://not.available", "theApp", appDir.Parent.FullName))
        {
          var (firstRelease, deltaEntry, fullEntry, currentRelease) = (IList<ReleaseEntry>)remoteAndLocalReleases.Remote;

          var updateInfo = updateMgr.CalculateUpdatePath(
            remoteAndLocalReleases,
            fullEntry,
            true);
          updateInfo.ReleasesToApply.Contains(fullEntry).ShouldBeTrue();
          updateInfo.ReleasesToApply.Count.ShouldEqual(1);

          await TestProgress(p => updateMgr.ApplyReleases(updateInfo, p));

          TestFileVersion(Path.Combine(appDir.FullName, "app-1.1.0.0"),
                          ("NLog.dll", new Version("2.0.0.0")),
                          ("NSync.Core.dll", new Version("1.1.0.0")));
        }
      }

      [Fact]
      public async Task ApplyReleasesWithTargetInferiorToCurrentVersionWithFullAndDeltaPkg()
      {
        using (SetupFixture(2, out var appDir, out var packagesDir,
                            out var remoteAndLocalReleases,
                            "Squirrel.Core.1.0.0.0-full.nupkg",
                            "Squirrel.Core.1.1.0.0-delta.nupkg",
                            "Squirrel.Core.1.2.0.0-full.nupkg"))
        using (var updateMgr = new UpdateManager("https://not.available", "theApp", appDir.Parent.FullName))
        {
          var (firstRelease, deltaEntry, currentRelease) = (IList<ReleaseEntry>)remoteAndLocalReleases.Remote;

          var updateInfo = updateMgr.CalculateUpdatePath(
            remoteAndLocalReleases,
            deltaEntry,
            true);
          updateInfo.ReleasesToApply.Contains(firstRelease).ShouldBeTrue();
          updateInfo.ReleasesToApply.Contains(deltaEntry).ShouldBeTrue();
          updateInfo.ReleasesToApply.Count.ShouldEqual(2);

          await TestProgress(p => updateMgr.ApplyReleases(updateInfo, p));

          TestFileVersion(Path.Combine(appDir.FullName, "app-1.1.0.0"),
                          ("NLog.dll", new Version("2.0.0.0")),
                          ("NSync.Core.dll", new Version("1.1.0.0")));
        }
      }

    [Fact]
    public async Task ApplyReleaseWhichMovesAFileToADifferentDirectory()
    {
      using (SetupFixture(0, out var appDir, out var packagesDir,
                          out var remoteAndLocalReleases,
                          "Squirrel.Core.1.1.0.0-full.nupkg",
                          "Squirrel.Core.1.3.0.0-full.nupkg"))
      using (var updateMgr = new UpdateManager("https://not.available", "theApp", appDir.Parent.FullName))
      {
        var (currentRelease, latestFullEntry) = (IList<ReleaseEntry>)remoteAndLocalReleases.Remote;
        
        var updateInfo = updateMgr.CalculateUpdatePath(
          remoteAndLocalReleases,
          latestFullEntry,
          true);
        updateInfo.ReleasesToApply.Contains(latestFullEntry).ShouldBeTrue();
        updateInfo.ReleasesToApply.Count.ShouldEqual(1);

        await TestProgress(p => updateMgr.ApplyReleases(updateInfo, p));

        var rootDirectory = Path.Combine(appDir.FullName, "app-1.3.0.0");

        TestFileVersion(rootDirectory,
                        ("NLog.dll", new Version("2.0.0.0")),
                        ("NSync.Core.dll", new Version("1.1.0.0")));

        var oldFile = Path.Combine(rootDirectory, "sub", "Ionic.Zip.dll");
        File.Exists(oldFile).ShouldBeFalse();

        var newFile = Path.Combine(rootDirectory, "other", "Ionic.Zip.dll");
        File.Exists(newFile).ShouldBeTrue();
      }
    }

    [Fact]
    public async Task ApplyReleaseWhichRemovesAFile()
    {
      using (SetupFixture(0, out var appDir, out var packagesDir,
                          out var remoteAndLocalReleases,
                          "Squirrel.Core.1.1.0.0-full.nupkg",
                          "Squirrel.Core.1.2.0.0-full.nupkg"))
      using (var updateMgr = new UpdateManager("https://not.available", "theApp", appDir.Parent.FullName))
      {
        var (currentRelease, latestFullEntry) = (IList<ReleaseEntry>)remoteAndLocalReleases.Remote;
        
        var updateInfo = updateMgr.CalculateUpdatePath(
          remoteAndLocalReleases,
          latestFullEntry,
          true);
        updateInfo.ReleasesToApply.Contains(latestFullEntry).ShouldBeTrue();
        updateInfo.ReleasesToApply.Count.ShouldEqual(1);

        await TestProgress(p => updateMgr.ApplyReleases(updateInfo, p));

        var rootDirectory = Path.Combine(appDir.FullName, "app-1.2.0.0");

        TestFileVersion(rootDirectory,
                        ("NLog.dll", new Version("2.0.0.0")),
                        ("NSync.Core.dll", new Version("1.1.0.0")));

        var oldFile = Path.Combine(rootDirectory, "sub", "Ionic.Zip.dll");
        File.Exists(oldFile).ShouldBeFalse();
      }
    }

    [Fact]
    public async Task CleanInstallRunsSquirrelAwareAppsWithInstallFlag()
    {
      string tempDir;
      string remotePkgDir;

      using (Utility.WithTempDirectory(out tempDir))
      using (Utility.WithTempDirectory(out remotePkgDir))
      {
        IntegrationTestHelper.CreateFakeInstalledApp("0.1.0", remotePkgDir);
        var pkgs = ReleaseEntry.BuildReleasesFile(remotePkgDir);
        ReleaseEntry.WriteReleaseFile(pkgs, Path.Combine(remotePkgDir, "RELEASES"));

        using (var fixture = new UpdateManager(remotePkgDir, "theApp", tempDir))
        {
          await fixture.FullInstall();

          // NB: We execute the Squirrel-aware apps, so we need to give
          // them a minute to settle or else the using statement will
          // try to blow away a running process
          await Task.Delay(1000);

          Assert.False(File.Exists(Path.Combine(tempDir, "theApp", "app-0.1.0", "args2.txt")));
          Assert.True(File.Exists(Path.Combine(tempDir, "theApp", "app-0.1.0", "args.txt")));

          var text = File.ReadAllText(Path.Combine(tempDir, "theApp", "app-0.1.0", "args.txt"), Encoding.UTF8);
          Assert.Contains("firstrun", text);
        }
      }
    }

    [Fact]
    public async Task CreateFullPackagesFromDeltaSmokeTest()
    {
      using (SetupFixture(0, out var appDir, out var packagesDir,
                          out var remoteAndLocalReleases,
                          "Squirrel.Core.1.0.0.0-full.nupkg",
                          "Squirrel.Core.1.1.0.0-delta.nupkg"))
      using (var updateMgr = new UpdateManager("https://not.available", "theApp", appDir.Parent.FullName))
      {
        var (currentRelease, deltaEntry) = (IList<ReleaseEntry>)remoteAndLocalReleases.Remote;
        
        var updateInfo = updateMgr.CalculateUpdatePath(
          remoteAndLocalReleases,
          deltaEntry,
          true);
        updateInfo.ReleasesToApply.Contains(deltaEntry).ShouldBeTrue();
        updateInfo.ReleasesToApply.Count.ShouldEqual(1);

        var fixture = new UpdateManager.ApplyReleasesImpl(appDir.FullName);

        var resultObs = (Task<ReleaseEntry>)fixture
                                            .GetType().GetMethod("CreateFullPackagesFromDeltas",
                                                                 BindingFlags.NonPublic | BindingFlags.Instance)
                                            .Invoke(fixture, new object[] { new[] { deltaEntry }, currentRelease });

        var result = await resultObs;
        var zp     = new ZipPackage(Path.Combine(packagesDir.FullName, result.Filename));

        zp.Version.ToString().ShouldEqual("1.1.0.0");
      }
    }

    [Fact]
    public async Task CreateShortcutsRoundTrip()
    {
      string remotePkgPath;
      string path;

      using (Utility.WithTempDirectory(out path))
      {
        using (Utility.WithTempDirectory(out remotePkgPath))
        using (var mgr = new UpdateManager(remotePkgPath, "theApp", path))
        {
          IntegrationTestHelper.CreateFakeInstalledApp("1.0.0.1", remotePkgPath);
          await mgr.FullInstall();
        }

        var fixture = new UpdateManager.ApplyReleasesImpl(Path.Combine(path, "theApp"));
        fixture.CreateShortcutsForExecutable("SquirrelAwareApp.exe",
                                             ShortcutLocation.Desktop | ShortcutLocation.StartMenu | ShortcutLocation.Startup
                                             | ShortcutLocation.AppRoot, false, null, null);

        // NB: COM is Weird.
        Thread.Sleep(1000);
        fixture.RemoveShortcutsForExecutable("SquirrelAwareApp.exe",
                                             ShortcutLocation.Desktop | ShortcutLocation.StartMenu | ShortcutLocation.Startup
                                             | ShortcutLocation.AppRoot);

        // NB: Squirrel-Aware first-run might still be running, slow
        // our roll before blowing away the temp path
        Thread.Sleep(1000);
      }
    }

    [Fact]
    public async Task FullUninstallRemovesAllVersions()
    {
      string tempDir;
      string remotePkgDir;

      using (Utility.WithTempDirectory(out tempDir))
      using (Utility.WithTempDirectory(out remotePkgDir))
      {
        IntegrationTestHelper.CreateFakeInstalledApp("0.1.0", remotePkgDir);
        var pkgs = ReleaseEntry.BuildReleasesFile(remotePkgDir);
        ReleaseEntry.WriteReleaseFile(pkgs, Path.Combine(remotePkgDir, "RELEASES"));

        using (var fixture = new UpdateManager(remotePkgDir, "theApp", tempDir))
          await fixture.FullInstall();

        await Task.Delay(1000);

        IntegrationTestHelper.CreateFakeInstalledApp("0.2.0", remotePkgDir);
        pkgs = ReleaseEntry.BuildReleasesFile(remotePkgDir);
        ReleaseEntry.WriteReleaseFile(pkgs, Path.Combine(remotePkgDir, "RELEASES"));

        using (var fixture = new UpdateManager(remotePkgDir, "theApp", tempDir))
          await fixture.UpdateApp(true);

        await Task.Delay(1000);

        using (var fixture = new UpdateManager(remotePkgDir, "theApp", tempDir))
          await fixture.FullUninstall();

        Assert.False(File.Exists(Path.Combine(tempDir, "theApp", "app-0.1.0", "args.txt")));
        Assert.False(File.Exists(Path.Combine(tempDir, "theApp", "app-0.2.0", "args.txt")));
        Assert.True(File.Exists(Path.Combine(tempDir, "theApp", ".dead")));
      }
    }

    [Fact(Skip = "This test is currently failing in CI")]
    public async Task GetShortcutsSmokeTest()
    {
      string remotePkgPath;
      string path;

      using (Utility.WithTempDirectory(out path))
      {
        using (Utility.WithTempDirectory(out remotePkgPath))
        using (var mgr = new UpdateManager(remotePkgPath, "theApp", path))
        {
          IntegrationTestHelper.CreateFakeInstalledApp("1.0.0.1", remotePkgPath);
          await mgr.FullInstall();
        }

        var fixture = new UpdateManager.ApplyReleasesImpl(Path.Combine(path, "theApp"));
        var result = fixture.GetShortcutsForExecutable("SquirrelAwareApp.exe",
                                                       ShortcutLocation.Desktop | ShortcutLocation.StartMenu | ShortcutLocation.Startup,
                                                       null);

        Assert.Equal(3, result.Keys.Count);

        // NB: Squirrel-Aware first-run might still be running, slow
        // our roll before blowing away the temp path
        Thread.Sleep(1000);
      }
    }

    [Fact]
    public async Task RunningUpgradeAppTwiceDoesntCrash()
    {
      string tempDir;
      string remotePkgDir;

      using (Utility.WithTempDirectory(out tempDir))
      using (Utility.WithTempDirectory(out remotePkgDir))
      {
        IntegrationTestHelper.CreateFakeInstalledApp("0.1.0", remotePkgDir);
        var pkgs = ReleaseEntry.BuildReleasesFile(remotePkgDir);
        ReleaseEntry.WriteReleaseFile(pkgs, Path.Combine(remotePkgDir, "RELEASES"));

        using (var fixture = new UpdateManager(remotePkgDir, "theApp", tempDir))
          await fixture.FullInstall();

        await Task.Delay(1000);

        IntegrationTestHelper.CreateFakeInstalledApp("0.2.0", remotePkgDir);
        pkgs = ReleaseEntry.BuildReleasesFile(remotePkgDir);
        ReleaseEntry.WriteReleaseFile(pkgs, Path.Combine(remotePkgDir, "RELEASES"));

        using (var fixture = new UpdateManager(remotePkgDir, "theApp", tempDir))
          await fixture.UpdateApp(true);

        await Task.Delay(1000);

        // NB: The 2nd time we won't have any updates to apply. We should just do nothing!
        using (var fixture = new UpdateManager(remotePkgDir, "theApp", tempDir))
          await fixture.UpdateApp(true);

        await Task.Delay(1000);
      }
    }

    [Fact]
    public void UnshimOurselvesSmokeTest()
    {
      // NB: This smoke test is really more of a manual test - try it
      // by shimming Slack, then verifying the shim goes away
      var appDir  = Environment.ExpandEnvironmentVariables(@"%LocalAppData%\Slack");
      var fixture = new UpdateManager.ApplyReleasesImpl(appDir);

      fixture.unshimOurselves();
    }

    [Fact]
    public async Task UpgradeRunsSquirrelAwareAppsWithUpgradeFlag()
    {
      string tempDir;
      string remotePkgDir;

      using (Utility.WithTempDirectory(out tempDir))
      using (Utility.WithTempDirectory(out remotePkgDir))
      {
        IntegrationTestHelper.CreateFakeInstalledApp("0.1.0", remotePkgDir);
        var pkgs = ReleaseEntry.BuildReleasesFile(remotePkgDir);
        ReleaseEntry.WriteReleaseFile(pkgs, Path.Combine(remotePkgDir, "RELEASES"));

        using (var fixture = new UpdateManager(remotePkgDir, "theApp", tempDir))
          await fixture.FullInstall();

        await Task.Delay(1000);

        IntegrationTestHelper.CreateFakeInstalledApp("0.2.0", remotePkgDir);
        pkgs = ReleaseEntry.BuildReleasesFile(remotePkgDir);
        ReleaseEntry.WriteReleaseFile(pkgs, Path.Combine(remotePkgDir, "RELEASES"));

        using (var fixture = new UpdateManager(remotePkgDir, "theApp", tempDir))
          await fixture.UpdateApp(true);

        await Task.Delay(1000);

        Assert.False(File.Exists(Path.Combine(tempDir, "theApp", "app-0.2.0", "args2.txt")));
        Assert.True(File.Exists(Path.Combine(tempDir, "theApp", "app-0.2.0", "args.txt")));

        var text = File.ReadAllText(Path.Combine(tempDir, "theApp", "app-0.2.0", "args.txt"), Encoding.UTF8);
        Assert.Contains("updated|0.2.0", text);
      }
    }

    [Fact]
    public void WhenNoNewReleasesAreAvailableTheListIsEmpty()
    {
      string tempDir;
      using (Utility.WithTempDirectory(out tempDir))
      {
        var appDir   = Directory.CreateDirectory(Path.Combine(tempDir, "theApp"));
        var packages = Path.Combine(appDir.FullName, "packages");
        Directory.CreateDirectory(packages);

        var package = "Squirrel.Core.1.0.0.0-full.nupkg";
        File.Copy(IntegrationTestHelper.GetPath("fixtures", package), Path.Combine(packages, package));

        var aGivenPackage = Path.Combine(packages, package);
        var baseEntry     = ReleaseEntry.GenerateFromFile(aGivenPackage);

        var updateInfo = UpdateInfo.Create(baseEntry, baseEntry, new HashSet<ReleaseEntry> { baseEntry }, "dontcare", true);

        Assert.Empty(updateInfo.ReleasesToApply);
      }
    }
  }
}
