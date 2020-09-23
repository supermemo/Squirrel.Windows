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
// Created On:   2020/09/13 10:17
// Modified On:  2020/09/13 10:17
// Modified By:  Alexis

#endregion




namespace Squirrel.Update
{
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.IO.Compression;
  using System.Linq;
  using System.Reflection;
  using System.Resources;
  using System.Text;
  using System.Text.RegularExpressions;
  using System.Threading;
  using System.Threading.Tasks;
  using Extensions;
  using global::Update;
  using Mono.Cecil;
  using NuGet;
  using Splat;

  /// <summary>
  /// The program.
  /// </summary>
  internal partial class Program
  {
    #region Methods

    public void Releasify(string package,
                          string targetDir           = null,
                          string packagesDir         = null,
                          string bootstrapperExe     = null,
                          string backgroundGif       = null,
                          string signingOpts         = null,
                          string baseUrl             = null,
                          string updateUrl           = null,
                          string setupIcon           = null,
                          bool   generateMsi         = true,
                          bool   packageAs64Bit      = false,
                          string frameworkVersion    = null,
                          string exeStubRegexPattern = null,
                          bool   generateDeltas      = true)
    {
      ensureConsole();

      if (baseUrl != null)
      {
        if (!Utility.IsHttpUrl(baseUrl))
          throw new Exception(string.Format("Invalid --baseUrl '{0}'. A base URL must start with http or https and be a valid URI.",
                                            baseUrl));

        if (!baseUrl.EndsWith("/"))
          baseUrl += "/";
      }

      if (updateUrl != null)
      {
        if (!Utility.IsHttpUrl(updateUrl))
          throw new Exception(string.Format("Invalid --updateUrl '{0}'. A base URL must start with http or https and be a valid URI.",
                                            updateUrl));

        if (!updateUrl.EndsWith("/"))
          updateUrl += "/";
      }

      targetDir       = targetDir ?? Path.Combine(".", "Releases");
      packagesDir     = packagesDir ?? ".";
      bootstrapperExe = bootstrapperExe ?? Path.Combine(".", "Setup.exe");

      if (!Directory.Exists(targetDir))
        Directory.CreateDirectory(targetDir);

      if (!File.Exists(bootstrapperExe))
        bootstrapperExe = Path.Combine(
          Path.GetDirectoryName(Assembly.GetEntryAssembly().Location),
          "Setup.exe");

      this.Log().Info("Bootstrapper EXE found at:" + bootstrapperExe);

      var di = new DirectoryInfo(targetDir);
      File.Copy(package, Path.Combine(di.FullName, Path.GetFileName(package)), true);

      var allNuGetFiles = di.EnumerateFiles()
                            .Where(x => x.Name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));

      var toProcess = allNuGetFiles.Where(x => !x.Name.Contains("-delta") && !x.Name.Contains("-full"));
      var processed = new List<string>();

      var releaseFilePath  = Path.Combine(di.FullName, "RELEASES");
      var previousReleases = new List<ReleaseEntry>();

      if (File.Exists(releaseFilePath))
        previousReleases.AddRange(ReleaseEntry.ParseReleaseFile(File.ReadAllText(releaseFilePath, Encoding.UTF8)));

      Regex exeStubRegex = null;

      if (string.IsNullOrWhiteSpace(exeStubRegexPattern) == false)
        exeStubRegex = new Regex(exeStubRegexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

      foreach (var file in toProcess)
      {
        this.Log().Info("Creating release package: " + file.FullName);

        var rp = new ReleasePackage(file.FullName);
        rp.CreateReleasePackage(Path.Combine(di.FullName, rp.SuggestedReleaseFileName), packagesDir, contentsPostProcessHook: pkgPath =>
        {
          new DirectoryInfo(pkgPath).GetAllFilesRecursively()
                                    .Where(x => x.Name.ToLowerInvariant().EndsWith(".exe"))
                                    .Where(x => !x.Name.ToLowerInvariant().Contains("update.exe"))
                                    .Where(x => !x.Name.ToLowerInvariant().Contains("squirrel.exe"))
                                    .Where(x => Utility.IsFileTopLevelInPackage(x.FullName, pkgPath))
                                    .Where(x => Utility.ExecutableUsesWin32Subsystem(x.FullName))
                                    .Where(x => exeStubRegex?.IsMatch(x.Name) ?? true)
                                    .ForEachAsync(x => createExecutableStubForExe(x.FullName))
                                    .Wait();

          if (signingOpts == null) return;

          new DirectoryInfo(pkgPath).GetAllFilesRecursively()
                                    .Where(x => Utility.FileIsLikelyPEImage(x.Name))
                                    .ForEachAsync(async x =>
                                    {
                                      if (isPEFileSigned(x.FullName))
                                      {
                                        this.Log().Info("{0} is already signed, skipping", x.FullName);
                                        return;
                                      }

                                      this.Log().Info("About to sign {0}", x.FullName);
                                      await signPEFile(x.FullName, signingOpts);
                                    }, 1)
                                    .Wait();
        });

        processed.Add(rp.ReleasePackageFile);

        var prev = ReleaseEntry.GetPreviousRelease(previousReleases, rp, targetDir);
        if (prev != null && generateDeltas)
        {
          var deltaBuilder = new DeltaPackageBuilder(null);

          var dp = deltaBuilder.CreateDeltaPackage(prev, rp,
                                                   Path.Combine(di.FullName, rp.SuggestedReleaseFileName.Replace("full", "delta")));
          processed.Insert(0, dp.InputPackageFile);
        }
      }

      foreach (var file in toProcess)
        File.Delete(file.FullName);

      var newReleaseEntries = processed
                              .Select(packageFilename => ReleaseEntry.GenerateFromFile(packageFilename, baseUrl))
                              .ToList();
      var distinctPreviousReleases = previousReleases
        .Where(x => !newReleaseEntries.Select(e => e.Version).Contains(x.Version));
      var releaseEntries    = distinctPreviousReleases.Concat(newReleaseEntries).ToList();
      var newestFullRelease = releaseEntries.MaxBy(x => x.Version).Where(x => !x.IsDelta).First();

      ReleaseEntry.WriteReleaseFile(releaseEntries, releaseFilePath);

      var targetSetupExe = Path.Combine(di.FullName, $"{newestFullRelease.PackageName}-Setup-{newestFullRelease.Version}.exe");

      File.Copy(bootstrapperExe, targetSetupExe, true);

      var zipPath = createSetupEmbeddedZip(
        Path.Combine(di.FullName, newestFullRelease.Filename),
        newestFullRelease.PackageName,
        newestFullRelease.GetTitle(di.FullName),
        updateUrl,
        backgroundGif,
        signingOpts,
        setupIcon).Result;

      var writeZipToSetup = Utility.FindHelperExecutable("WriteZipToSetup.exe");

      try
      {
        var arguments = string.Format("\"{0}\" \"{1}\" \"--set-required-framework\" \"{2}\"", targetSetupExe, zipPath, frameworkVersion);
        var result    = Utility.InvokeProcessAsync(writeZipToSetup, arguments, CancellationToken.None).Result;
        if (result.Item1 != 0) throw new Exception("Failed to write Zip to Setup.exe!\n\n" + result.Item2);
      }
      catch (Exception ex)
      {
        this.Log().ErrorException("Failed to update Setup.exe with new Zip file", ex);
      }
      finally
      {
        File.Delete(zipPath);
      }

      Utility.Retry(() =>
                      setPEVersionInfoAndIcon(targetSetupExe, new ZipPackage(package), setupIcon).Wait());

      if (signingOpts != null)
        signPEFile(targetSetupExe, signingOpts).Wait();

      if (generateMsi)
      {
        createMsiPackage(targetSetupExe, new ZipPackage(package), packageAs64Bit).Wait();

        if (signingOpts != null)
          signPEFile(targetSetupExe.Replace(".exe", ".msi"), signingOpts).Wait();
      }
    }

    private void WriteUpdaterResources(
      string srcUpdateExeFilePath,
      string destUpdateExeFilePath,
      string packageName,
      string appTitle,
      string updateUrl)
    {
      var symbolFileNameWithoutExt = Path.GetFileNameWithoutExtension(ModeDetector.InUnitTestRunner()
                                                                        ? Assembly.GetExecutingAssembly().Location
                                                                        : Assembly.GetEntryAssembly().Location);
      var symbolFilePath = Utility.FindHelperExecutable(symbolFileNameWithoutExt + ".pdb");

      using (var symbolStream = File.OpenRead(symbolFilePath))
      using (var module =
        AssemblyDefinition.ReadAssembly(srcUpdateExeFilePath, new ReaderParameters { ReadSymbols = true, SymbolStream = symbolStream }))
      using (var ms = new MemoryStream())
      {
        using (var writer = new ResourceWriter(ms))
        {
          writer.AddResource(nameof(Resources.AppTitle), appTitle);
          writer.AddResource(nameof(Resources.BaseUrl), updateUrl);
          writer.AddResource(nameof(Resources.PackageName), packageName);

          writer.Generate();
        }

        var resName = typeof(Resources).FullName + ".resources";

        for (var i = 0; i < module.MainModule.Resources.Count; i++)
          if (module.MainModule.Resources[i].Name.Equals(resName, StringComparison.InvariantCultureIgnoreCase))
          {
            module.MainModule.Resources.RemoveAt(i);
            break;
          }

        var er = new EmbeddedResource(resName, ManifestResourceAttributes.Public, ms.ToArray());

        module.MainModule.Resources.Add(er);
        module.Write(destUpdateExeFilePath, new WriterParameters { WriteSymbols = true });
      }
    }

    private async Task<string> createSetupEmbeddedZip(
      string fullPackage,
      string packageName,
      string appTitle,
      string updateUrl,
      string backgroundGif,
      string signingOpts,
      string setupIcon)
    {
      string tempPath;

      this.Log().Info("Building embedded zip file for Setup.exe");
      using (Utility.WithTempDirectory(out tempPath, null))
      {
        this.ErrorIfThrows(() =>
        {
          var srcUpdateExe    = Assembly.GetEntryAssembly().Location.Replace("-Mono.exe", ".exe");
          var targetUpdateExe = Path.Combine(tempPath, "Update.exe");

          WriteUpdaterResources(srcUpdateExe, targetUpdateExe, packageName, appTitle, updateUrl);
        }, "Failed to write package file to temp dir: " + tempPath);

        this.ErrorIfThrows(() => { File.Copy(fullPackage, Path.Combine(tempPath, Path.GetFileName(fullPackage))); },
                           "Failed to write package file to temp dir: " + tempPath);

        if (!string.IsNullOrWhiteSpace(backgroundGif))
          this.ErrorIfThrows(() => { File.Copy(backgroundGif, Path.Combine(tempPath, "background.gif")); },
                             "Failed to write animated GIF to temp dir: " + tempPath);

        if (!string.IsNullOrWhiteSpace(setupIcon))
          this.ErrorIfThrows(() => { File.Copy(setupIcon, Path.Combine(tempPath, "setupIcon.ico")); },
                             "Failed to write icon to temp dir: " + tempPath);

        var releases = new[] { ReleaseEntry.GenerateFromFile(fullPackage) };
        ReleaseEntry.WriteReleaseFile(releases, Path.Combine(tempPath, "RELEASES"));

        var target = Path.GetTempFileName();
        File.Delete(target);

        // Sign Update.exe so that virus scanners don't think we're
        // pulling one over on them
        if (signingOpts != null)
        {
          var di = new DirectoryInfo(tempPath);

          var files = di.EnumerateFiles()
                        .Where(x => x.Name.ToLowerInvariant().EndsWith(".exe"))
                        .Select(x => x.FullName);

          await files.ForEachAsync(x => signPEFile(x, signingOpts));
        }

        this.ErrorIfThrows(() =>
                             ZipFile.CreateFromDirectory(tempPath, target, CompressionLevel.Optimal, false),
                           "Failed to create Zip file from directory: " + tempPath);

        return target;
      }
    }

    private static async Task signPEFile(string exePath, string signingOpts)
    {
      // Try to find SignTool.exe
      var exe = @".\signtool.exe";
      if (!File.Exists(exe))
      {
        exe = Path.Combine(
          Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
          "signtool.exe");

        // Run down PATH and hope for the best
        if (!File.Exists(exe)) exe = "signtool.exe";
      }

      var processResult = await Utility.InvokeProcessAsync(exe,
                                                           string.Format("sign {0} \"{1}\"", signingOpts, exePath), CancellationToken.None);

      if (processResult.Item1 != 0)
      {
        var optsWithPasswordHidden = new Regex(@"/p\s+\w+").Replace(signingOpts, "/p ********");
        var msg = string.Format("Failed to sign, command invoked was: '{0} sign {1} {2}'",
                                exe, optsWithPasswordHidden, exePath);

        throw new Exception(msg);
      }
      else
      {
        Console.WriteLine(processResult.Item2);
      }
    }

    private bool isPEFileSigned(string path)
    {
#if MONO
            return Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);
#else
      try
      {
        return AuthenticodeTools.IsTrusted(path);
      }
      catch (Exception ex)
      {
        this.Log().ErrorException("Failed to determine signing status for " + path, ex);
        return false;
      }

#endif
    }

    private async Task createExecutableStubForExe(string fullName)
    {
      var exe = Utility.FindHelperExecutable(@"StubExecutable.exe");

      var target = Path.Combine(
        Path.GetDirectoryName(fullName),
        Path.GetFileNameWithoutExtension(fullName) + "_ExecutionStub.exe");

      await Utility.CopyToAsync(exe, target);

      await Utility.InvokeProcessAsync(
        Utility.FindHelperExecutable("WriteZipToSetup.exe"),
        string.Format("--copy-stub-resources \"{0}\" \"{1}\"", fullName, target),
        CancellationToken.None);
    }

    private static async Task setPEVersionInfoAndIcon(string exePath, IPackage package, string iconPath = null)
    {
      var realExePath = Path.GetFullPath(exePath);
      var company     = string.Join(",", package.Authors);
      var verStrings = new Dictionary<string, string>()
      {
        { "CompanyName", company },
        { "LegalCopyright", package.Copyright ?? "Copyright © " + DateTime.Now.Year.ToString() + " " + company },
        { "FileDescription", package.Summary ?? package.Description ?? "Installer for " + package.Id },
        { "ProductName", package.Description ?? package.Summary ?? package.Id },
      };

      var args = verStrings.Aggregate(new StringBuilder("\"" + realExePath + "\""), (acc, x) =>
      {
        acc.AppendFormat(" --set-version-string \"{0}\" \"{1}\"", x.Key, x.Value);
        return acc;
      });
      args.AppendFormat(" --set-file-version {0} --set-product-version {0}", package.Version.ToString());
      if (iconPath != null)
        args.AppendFormat(" --set-icon \"{0}\"", Path.GetFullPath(iconPath));

      // Try to find rcedit.exe
      string exe = Utility.FindHelperExecutable("rcedit.exe");

      var processResult = await Utility.InvokeProcessAsync(exe, args.ToString(), CancellationToken.None);

      if (processResult.Item1 != 0)
      {
        var msg = string.Format(
          "Failed to modify resources, command invoked was: '{0} {1}'\n\nOutput was:\n{2}",
          exe, args, processResult.Item2);

        throw new Exception(msg);
      }
      else
      {
        Console.WriteLine(processResult.Item2);
      }
    }

    private static async Task createMsiPackage(string setupExe, IPackage package, bool packageAs64Bit)
    {
      var pathToWix   = pathToWixTools();
      var setupExeDir = Path.GetDirectoryName(setupExe);
      var company     = string.Join(",", package.Authors);

      var culture = CultureInfo.GetCultureInfo(package.Language ?? string.Empty).TextInfo.ANSICodePage;


      var templateText = File.ReadAllText(Path.Combine(pathToWix, "template.wxs"));
      var templateData = new Dictionary<string, string>
      {
        { "Id", package.Id },
        { "Title", package.Title },
        { "Author", company },
        { "Version", Regex.Replace(package.Version.ToString(), @"-.*$", string.Empty) },
        { "Summary", package.Summary ?? package.Description ?? package.Id },
        { "Codepage", $"{culture}" },
        { "Platform", packageAs64Bit ? "x64" : "x86" },
        { "ProgramFilesFolder", packageAs64Bit ? "ProgramFiles64Folder" : "ProgramFilesFolder" },
        { "Win64YesNo", packageAs64Bit ? "yes" : "no" }
      };

      // NB: We need some GUIDs that are based on the package ID, but unique (i.e.
      // "Unique but consistent").
      for (int i = 1; i <= 10; i++)
        templateData[string.Format("IdAsGuid{0}", i)] = Utility.CreateGuidFromHash(string.Format("{0}:{1}", package.Id, i)).ToString();

      var templateResult = CopStache.Render(templateText, templateData);

      var wxsTarget = Path.Combine(setupExeDir, "Setup.wxs");
      File.WriteAllText(wxsTarget, templateResult, Encoding.UTF8);

      var candleParams = string.Format("-nologo -ext WixNetFxExtension -out \"{0}\" \"{1}\"", wxsTarget.Replace(".wxs", ".wixobj"),
                                       wxsTarget);
      var processResult = await Utility.InvokeProcessAsync(
        Path.Combine(pathToWix, "candle.exe"), candleParams, CancellationToken.None, setupExeDir);

      if (processResult.Item1 != 0)
      {
        var msg = string.Format(
          "Failed to compile WiX template, command invoked was: '{0} {1}'\n\nOutput was:\n{2}",
          "candle.exe", candleParams, processResult.Item2);

        throw new Exception(msg);
      }

      var lightParams = string.Format("-ext WixNetFxExtension -sval -out \"{0}\" \"{1}\"", wxsTarget.Replace(".wxs", ".msi"),
                                      wxsTarget.Replace(".wxs", ".wixobj"));
      processResult = await Utility.InvokeProcessAsync(
        Path.Combine(pathToWix, "light.exe"), lightParams, CancellationToken.None, setupExeDir);

      if (processResult.Item1 != 0)
      {
        var msg = string.Format(
          "Failed to link WiX template, command invoked was: '{0} {1}'\n\nOutput was:\n{2}",
          "light.exe", lightParams, processResult.Item2);

        throw new Exception(msg);
      }

      var toDelete = new[]
      {
        wxsTarget,
        wxsTarget.Replace(".wxs", ".wixobj"),
        wxsTarget.Replace(".wxs", ".wixpdb"),
      };

      await Utility.ForEachAsync(toDelete, x => Utility.DeleteFileHarder(x));
    }

    private static string pathToWixTools()
    {
      var ourPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

      // Same Directory? (i.e. released)
      if (File.Exists(Path.Combine(ourPath, "candle.exe")))
        return ourPath;

      // Debug Mode (i.e. in vendor)
      var debugPath = Path.Combine(ourPath, "..", "..", "..", "vendor", "wix", "candle.exe");
      if (File.Exists(debugPath))
        return Path.GetFullPath(debugPath);

      throw new Exception("WiX tools can't be found");
    }

    #endregion
  }
}
