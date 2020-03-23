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
// Modified On:  2020/03/20 21:01
// Modified By:  Alexis

#endregion




using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Readers;
using Xunit;

namespace Squirrel.Tests.TestHelpers
{
  public static class IntegrationTestHelper
  {
    #region Constants & Statics

    private static object gate = 42;

    #endregion




    #region Methods

    public static string GetPath(params string[] paths)
    {
      var ret = GetIntegrationTestRootDirectory();
      return new FileInfo(paths.Aggregate(ret, Path.Combine)).FullName;
    }

    public static string GetIntegrationTestRootDirectory()
    {
      // XXX: This is an evil hack, but it's okay for a unit test
      // We can't use Assembly.Location because unit test runners love
      // to move stuff to temp directories
      var st = new StackFrame(true);
      var di = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(st.GetFileName()), ".."));

      return di.FullName;
    }

    public static bool SkipTestOnXPAndVista()
    {
      int osVersion = Environment.OSVersion.Version.Major * 100 + Environment.OSVersion.Version.Minor;
      return osVersion < 601;
    }

    public static void RunBlockAsSTA(Action block)
    {
      Exception ex = null;
      var t = new Thread(() =>
      {
        try
        {
          block();
        }
        catch (Exception e)
        {
          ex = e;
        }
      });

      t.SetApartmentState(ApartmentState.STA);
      t.Start();
      t.Join();

      if (ex != null) // NB: If we don't do this, the test silently passes
        throw new Exception("", ex);
    }

    public static IDisposable WithFakeInstallDirectory(string packageFileName, out string path)
    {
      var ret = Utility.WithTempDirectory(out path);

      File.Copy(GetPath("fixtures", packageFileName), Path.Combine(path, packageFileName));
      var rp = ReleaseEntry.GenerateFromFile(Path.Combine(path, packageFileName));
      ReleaseEntry.WriteReleaseFile(new[] { rp }, Path.Combine(path, "RELEASES"));

      return ret;
    }

    public static string CreateFakeInstalledApp(string version, string outputDir, string nuspecFile = null)
    {
      var targetDir = default(string);

      var nuget = GetPath("..", ".nuget", "nuget.exe");
      nuspecFile = nuspecFile ?? "SquirrelInstalledApp.nuspec";

      using (var clearTemp = Utility.WithTempDirectory(out targetDir))
      {
        var nuspec = File.ReadAllText(GetPath("fixtures", nuspecFile), Encoding.UTF8);
        File.WriteAllText(Path.Combine(targetDir, nuspecFile), nuspec.Replace("0.1.0", version), Encoding.UTF8);

        File.Copy(
          GetPath("fixtures", "SquirrelAwareApp.exe"),
          Path.Combine(targetDir, "SquirrelAwareApp.exe"));
        File.Copy(
          GetPath("fixtures", "NotSquirrelAwareApp.exe"),
          Path.Combine(targetDir, "NotSquirrelAwareApp.exe"));

        var psi = new ProcessStartInfo(nuget, "pack " + Path.Combine(targetDir, nuspecFile))
        {
          RedirectStandardError  = true,
          RedirectStandardOutput = true,
          UseShellExecute        = false,
          CreateNoWindow         = true,
          WorkingDirectory       = targetDir,
          WindowStyle            = ProcessWindowStyle.Hidden,
        };

        var pi = Process.Start(psi);
        pi.WaitForExit();
        var output = pi.StandardOutput.ReadToEnd();
        var err    = pi.StandardError.ReadToEnd();
        Console.WriteLine(output);
        Console.WriteLine(err);

        var di  = new DirectoryInfo(targetDir);
        var pkg = di.EnumerateFiles("*.nupkg").First();

        var targetPkgFile = Path.Combine(outputDir, pkg.Name);
        File.Copy(pkg.FullName, targetPkgFile);
        return targetPkgFile;
      }
    }

    public static IDisposable WithFakeInstallDirectory(out string path)
    {
      return WithFakeInstallDirectory("SampleUpdatingApp.1.1.0.0.nupkg", out path);
    }

    public static IDisposable WithFakeAlreadyInstalledApp(out string path)
    {
      return WithFakeAlreadyInstalledApp("InstalledSampleUpdatingApp-1.1.0.0.zip", out path);
    }

    public static IDisposable WithFakeAlreadyInstalledApp(string zipFile, out string path)
    {
      var ret = Utility.WithTempDirectory(out path);
      var zipPath = GetPath("fixtures", zipFile);

      WithFakeAlreadyInstalledApp(zipPath, path);

      return ret;
    }

    public static void WithFakeAlreadyInstalledApp(string zipPath, string path)
    {
      Directory.CreateDirectory(path);

      // NB: Apparently Ionic.Zip is perfectly content to extract a Zip
      // file that doesn't actually exist, without failing.
      Assert.True(File.Exists(zipPath));

      var opts = new ExtractionOptions { ExtractFullPath = true, Overwrite = true, PreserveFileTime = true };

      using (var za = ZipArchive.Open(zipPath))
      using (var reader = za.ExtractAllEntries())
        reader.WriteEntryToDirectory(path, opts);
    }

    #endregion
  }
}
