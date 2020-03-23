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
// Modified On:  2020/03/20 18:28
// Modified By:  Alexis

#endregion




using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Splat;
using Squirrel.Extensions;

namespace Squirrel.Tests.TestHelpers
{
  public abstract class TestBase : IEnableLogger
  {
    #region Methods

    protected IDisposable SetupFixture(int                           currentReleaseIndex,
                                       out    DirectoryInfo          appDir,
                                       out    DirectoryInfo          packages,
                                       out    RemoteAndLocalReleases remoteAndLocalReleases,
                                       params string[]               packageFiles)
    {
      var disposable     = SetupFixture(out appDir, out packages, out remoteAndLocalReleases, packageFiles);
      var currentRelease = remoteAndLocalReleases.Remote.ElementAt(currentReleaseIndex);

      remoteAndLocalReleases = new RemoteAndLocalReleases(
        remoteAndLocalReleases.Remote,
        new List<ReleaseEntry> { currentRelease },
        currentRelease);

      Directory.CreateDirectory(Path.Combine(appDir.FullName, "app-" + currentRelease.Version));
      /*IntegrationTestHelper.WithFakeAlreadyInstalledApp(
        Path.Combine(packages.FullName, packageFiles[currentReleaseIndex]),
        Path.Combine(appDir.FullName, "app-" + currentRelease.Version));*/

      return disposable;
    }

    protected IDisposable SetupFixture(out    DirectoryInfo          appDir,
                                       out    DirectoryInfo          packages,
                                       out    RemoteAndLocalReleases remoteAndLocalReleases,
                                       params string[]               packageFiles)
    {
      var disposable = Utility.WithTempDirectory(out var tempDir);
      appDir   = Directory.CreateDirectory(Path.Combine(tempDir, "theApp"));
      packages = Directory.CreateDirectory(Path.Combine(appDir.FullName, "packages"));

      var releaseEntries = new List<ReleaseEntry>();

      foreach (var pkgFile in packageFiles)
      {
        var pkgFilePath = Path.Combine(packages.FullName, pkgFile);

        File.Copy(IntegrationTestHelper.GetPath("fixtures", pkgFile), pkgFilePath);
        releaseEntries.Add(ReleaseEntry.GenerateFromFile(pkgFilePath));
      }

      remoteAndLocalReleases = new RemoteAndLocalReleases(releaseEntries, null, null);

      return disposable;
    }

    protected async Task TestProgress(Func<Action<int>, Task> action)
    {
      var progress = new List<int>();

      await action(progress.Add);

      this.Log().Info("Progress: [{0}]", string.Join(",", progress));

      progress.Aggregate(0, (acc, x) =>
              {
                (x >= acc).ShouldBeTrue();
                return x;
              })
              .ShouldEqual(100);
    }

    protected void TestFileVersion(string dirBase, params (string name, Version version)[] fileVersions)
    {
      fileVersions.ForEach(x =>
      {
        var path = Path.Combine(dirBase, x.name);

        this.Log().Info("Looking for {0}", path);
        File.Exists(path).ShouldBeTrue();

        var vi      = FileVersionInfo.GetVersionInfo(path);
        var verInfo = new Version(vi.FileVersion ?? "1.0.0.0");

        x.version.ShouldEqual(verInfo);
      });
    }

    #endregion
  }
}
