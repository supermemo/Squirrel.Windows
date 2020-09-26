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
// Created On:   2020/03/29 00:20
// Modified On:  2020/09/26 08:51
// Modified By:  Alexis

#endregion




namespace Squirrel.Tests
{
  using System;
  using System.Collections.Generic;
  using TestHelpers;
  using Xunit;

  /// <summary>
  /// The check for update tests.
  /// </summary>
  public class CheckForUpdateTests : TestBase
  {
    [Fact]
    public void CalculateUpdateInfoWithMinPreReleaseAndNoStableVersion()
    {
      using (SetupFixture(0, out var appDir, out var packagesDir,
                          out var remoteAndLocalReleases,
                          "SuperMemoAssistant-2.0.5-alpha.12-full.nupkg",
                          "SuperMemoAssistant-2.0.5-alpha.14-delta.nupkg",
                          "SuperMemoAssistant-2.0.5-alpha.14-full.nupkg"))
      using (var updateMgr = new UpdateManager("https://not.available", "theApp", appDir.Parent.FullName))
      {
        var (currentRelease, deltaEntryA14, fullEntryA14) =
          (IList<ReleaseEntry>)remoteAndLocalReleases.Remote;

        var updateInfo = updateMgr.CalculateUpdateInfo(
          UpdaterIntention.Update,
          remoteAndLocalReleases,
          false,
          true,
          "alpha");
        
        updateInfo.ReleasesToApply.Contains(deltaEntryA14).ShouldBeTrue();
        updateInfo.ReleasesToApply.Count.ShouldEqual(1);
      }
    }
    [Fact]
    public void CalculateUpdateInfoWithMinPreReleaseAndAStableVersion()
    {
      using (SetupFixture(0, out var appDir, out var packagesDir,
                          out var remoteAndLocalReleases,
                          "SuperMemoAssistant-2.0.5-alpha.12-full.nupkg",
                          "SuperMemoAssistant-2.0.5-alpha.14-delta.nupkg",
                          "SuperMemoAssistant-2.0.5-alpha.14-full.nupkg",
                          "SuperMemoAssistant-2.0.5-delta.nupkg",
                          "SuperMemoAssistant-2.0.5-full.nupkg"))
      using (var updateMgr = new UpdateManager("https://not.available", "theApp", appDir.Parent.FullName))
      {
        var (currentRelease, deltaEntryA14, fullEntryA14, deltaEntryStable, fullEntryStable) =
          (IList<ReleaseEntry>)remoteAndLocalReleases.Remote;

        var updateInfo = updateMgr.CalculateUpdateInfo(
          UpdaterIntention.Update,
          remoteAndLocalReleases,
          false,
          true,
          "alpha");
        
        updateInfo.ReleasesToApply.Contains(deltaEntryA14).ShouldBeTrue();
        updateInfo.ReleasesToApply.Contains(deltaEntryStable).ShouldBeTrue();
        updateInfo.ReleasesToApply.Count.ShouldEqual(2);
      }
    }
    
    [Fact]
    public void CalculateUpdateInfoWithStableMinPreRelease()
    {
      using (SetupFixture(0, out var appDir, out var packagesDir,
                          out var remoteAndLocalReleases,
                          "SuperMemoAssistant-2.0.5-alpha.12-full.nupkg",
                          "SuperMemoAssistant-2.0.5-alpha.14-delta.nupkg",
                          "SuperMemoAssistant-2.0.5-alpha.14-full.nupkg",
                          "SuperMemoAssistant-2.0.5-delta.nupkg",
                          "SuperMemoAssistant-2.0.5-full.nupkg"))
      using (var updateMgr = new UpdateManager("https://not.available", "theApp", appDir.Parent.FullName))
      {
        var (currentRelease, deltaEntryA14, fullEntryA14, deltaEntryStable, fullEntryStable) =
          (IList<ReleaseEntry>)remoteAndLocalReleases.Remote;

        var updateInfo = updateMgr.CalculateUpdateInfo(
          UpdaterIntention.Update,
          remoteAndLocalReleases,
          false,
          true,
          string.Empty);
        
        updateInfo.ReleasesToApply.Contains(deltaEntryA14).ShouldBeTrue();
        updateInfo.ReleasesToApply.Contains(deltaEntryStable).ShouldBeTrue();
        updateInfo.ReleasesToApply.Count.ShouldEqual(2);
      }
    }
    
    [Fact]
    public void CalculateUpdateInfoWithStableMinPreReleaseAndNoStableUpdate()
    {
      using (SetupFixture(0, out var appDir, out var packagesDir,
                          out var remoteAndLocalReleases,
                          "SuperMemoAssistant-2.0.5-alpha.12-full.nupkg",
                          "SuperMemoAssistant-2.0.5-alpha.14-delta.nupkg",
                          "SuperMemoAssistant-2.0.5-alpha.14-full.nupkg"))
      using (var updateMgr = new UpdateManager("https://not.available", "theApp", appDir.Parent.FullName))
      {
        var updateInfo = updateMgr.CalculateUpdateInfo(
          UpdaterIntention.Update,
          remoteAndLocalReleases,
          false,
          true,
          string.Empty);
        
        updateInfo.ReleasesToApply.Count.ShouldEqual(0);
      }
    }
    
    [Fact]
    public void CalculateUpdateInfoWithNoMinPreReleaseAndOneUpdate()
    {
      using (SetupFixture(0, out var appDir, out var packagesDir,
                          out var remoteAndLocalReleases,
                          "SuperMemoAssistant-2.0.5-alpha.12-full.nupkg",
                          "SuperMemoAssistant-2.0.5-alpha.14-delta.nupkg",
                          "SuperMemoAssistant-2.0.5-alpha.14-full.nupkg"))
      using (var updateMgr = new UpdateManager("https://not.available", "theApp", appDir.Parent.FullName))
      {
        var (currentRelease, deltaEntryA14, fullEntryA14) =
          (IList<ReleaseEntry>)remoteAndLocalReleases.Remote;

        var updateInfo = updateMgr.CalculateUpdateInfo(
          UpdaterIntention.Update,
          remoteAndLocalReleases,
          false,
          true,
          null);
        
        updateInfo.ReleasesToApply.Contains(deltaEntryA14).ShouldBeTrue();
        updateInfo.ReleasesToApply.Count.ShouldEqual(1);
      }
    }
    
    [Fact]
    public void CalculateUpdateInfoWithNoMinPreReleaseAndTwoUpdates()
    {
      using (SetupFixture(0, out var appDir, out var packagesDir,
                          out var remoteAndLocalReleases,
                          "SuperMemoAssistant-2.0.5-alpha.12-full.nupkg",
                          "SuperMemoAssistant-2.0.5-alpha.14-delta.nupkg",
                          "SuperMemoAssistant-2.0.5-alpha.14-full.nupkg",
                          "SuperMemoAssistant-2.0.5-delta.nupkg",
                          "SuperMemoAssistant-2.0.5-full.nupkg"))
      using (var updateMgr = new UpdateManager("https://not.available", "theApp", appDir.Parent.FullName))
      {
        var (currentRelease, deltaEntryA14, fullEntryA14, deltaEntryStable, fullEntryStable) =
          (IList<ReleaseEntry>)remoteAndLocalReleases.Remote;

        var updateInfo = updateMgr.CalculateUpdateInfo(
          UpdaterIntention.Update,
          remoteAndLocalReleases,
          false,
          true,
          null);
        
        updateInfo.ReleasesToApply.Contains(deltaEntryA14).ShouldBeTrue();
        updateInfo.ReleasesToApply.Contains(deltaEntryStable).ShouldBeTrue();
        updateInfo.ReleasesToApply.Count.ShouldEqual(2);
      }
    }

    /// <summary>
    /// The corrupted release file means we start from scratch.
    /// </summary>
    [Fact(Skip = "Rewrite this to be an integration test")]
    public void CorruptedReleaseFileMeansWeStartFromScratch()
    {
      Assert.False(true, "Rewrite this to be an integration test");

      /*
      string localPackagesDir = Path.Combine(".", "theApp", "packages");
      string localReleasesFile = Path.Combine(localPackagesDir, "RELEASES");

      var fileInfo = new Mock<FileInfoBase>();
      fileInfo.Setup(x => x.Exists).Returns(true);
      fileInfo.Setup(x => x.OpenRead())
          .Returns(new MemoryStream(Encoding.UTF8.GetBytes("lol this isn't right")));

      var dirInfo = new Mock<DirectoryInfoBase>();
      dirInfo.Setup(x => x.Exists).Returns(true);

      var fs = new Mock<IFileSystemFactory>();
      fs.Setup(x => x.GetFileInfo(localReleasesFile)).Returns(fileInfo.Object);
      fs.Setup(x => x.CreateDirectoryRecursive(localPackagesDir)).Verifiable();
      fs.Setup(x => x.DeleteDirectoryRecursive(localPackagesDir)).Verifiable();
      fs.Setup(x => x.GetDirectoryInfo(localPackagesDir)).Returns(dirInfo.Object);

      var urlDownloader = new Mock<IUrlDownloader>();
      var dlPath = IntegrationTestHelper.GetPath("fixtures", "RELEASES-OnePointOne");
      urlDownloader.Setup(x => x.DownloadUrl(It.IsAny<string>(), It.IsAny<IObserver<int>>()))
          .Returns(Observable.Return(File.ReadAllText(dlPath, Encoding.UTF8)));

      var fixture = new UpdateManager("http://lol", "theApp", ".", fs.Object, urlDownloader.Object);
      using (fixture) {
          fixture.CheckForUpdate().First();
      }

      fs.Verify(x => x.CreateDirectoryRecursive(localPackagesDir), Times.Once());
      fs.Verify(x => x.DeleteDirectoryRecursive(localPackagesDir), Times.Once());
      */
    }

    /// <summary>
    /// The corrupt remote file should throw on check.
    /// </summary>
    [Fact(Skip = "Rewrite this to be an integration test")]
    public void CorruptRemoteFileShouldThrowOnCheck()
    {
      Assert.False(true, "Rewrite this to be an integration test");

      /*
      string localPackagesDir = Path.Combine(".", "theApp", "packages");
      string localReleasesFile = Path.Combine(localPackagesDir, "RELEASES");

      var fileInfo = new Mock<FileInfoBase>();
      fileInfo.Setup(x => x.Exists).Returns(false);

      var dirInfo = new Mock<DirectoryInfoBase>();
      dirInfo.Setup(x => x.Exists).Returns(true);

      var fs = new Mock<IFileSystemFactory>();
      fs.Setup(x => x.GetFileInfo(localReleasesFile)).Returns(fileInfo.Object);
      fs.Setup(x => x.CreateDirectoryRecursive(localPackagesDir)).Verifiable();
      fs.Setup(x => x.DeleteDirectoryRecursive(localPackagesDir)).Verifiable();
      fs.Setup(x => x.GetDirectoryInfo(localPackagesDir)).Returns(dirInfo.Object);

      var urlDownloader = new Mock<IUrlDownloader>();
      urlDownloader.Setup(x => x.DownloadUrl(It.IsAny<string>(), It.IsAny<IObserver<int>>()))
          .Returns(Observable.Return("lol this isn't right"));

      var fixture = new UpdateManager("http://lol", "theApp", ".", fs.Object, urlDownloader.Object);

      using (fixture) {
          Assert.Throws<Exception>(() => fixture.CheckForUpdate().First());   
      }
      */
    }

    /// <summary>
    /// The if local and remote are equal then do nothing.
    /// </summary>
    /// <exception cref="NotImplementedException">
    /// </exception>
    [Fact(Skip = "TODO")]
    public void IfLocalAndRemoteAreEqualThenDoNothing()
    {
      throw new NotImplementedException();
    }

    /// <summary>
    /// The if local version greater than remote we rollback.
    /// </summary>
    /// <exception cref="NotImplementedException">
    /// </exception>
    [Fact(Skip = "TODO")]
    public void IfLocalVersionGreaterThanRemoteWeRollback()
    {
      throw new NotImplementedException();
    }

    /// <summary>
    /// The new releases should be detected.
    /// </summary>
    [Fact(Skip = "Rewrite this to be an integration test")]
    public void NewReleasesShouldBeDetected()
    {
      Assert.False(true, "Rewrite this to be an integration test");

/*
      string localReleasesFile = Path.Combine(".", "theApp", "packages", "RELEASES");

      var fileInfo = new Mock<FileInfoBase>();
      fileInfo.Setup(x => x.OpenRead())
          .Returns(File.OpenRead(IntegrationTestHelper.GetPath("fixtures", "RELEASES-OnePointOh")));

      var fs = new Mock<IFileSystemFactory>();
      fs.Setup(x => x.GetFileInfo(localReleasesFile)).Returns(fileInfo.Object);

      var urlDownloader = new Mock<IUrlDownloader>();
      var dlPath = IntegrationTestHelper.GetPath("fixtures", "RELEASES-OnePointOne");
      urlDownloader.Setup(x => x.DownloadUrl(It.IsAny<string>(), It.IsAny<IObserver<int>>()))
          .Returns(Observable.Return(File.ReadAllText(dlPath, Encoding.UTF8)));

      var fixture = new UpdateManager("http://lol", "theApp", ".", fs.Object, urlDownloader.Object);
      var result = default(UpdateInfo);

      using (fixture) {
          result = fixture.CheckForUpdate().First();
      }

      Assert.NotNull(result);
      Assert.Equal(1, result.ReleasesToApply.Single().Version.Major);
      Assert.Equal(1, result.ReleasesToApply.Single().Version.Minor);
      */
    }
  }
}
