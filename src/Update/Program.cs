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
// Modified On:  2020/03/20 23:14
// Modified By:  Alexis

#endregion




using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using System.Windows;
using Mono.Cecil;
using NuGet;
using Splat;
using Squirrel.Extensions;
using Squirrel.Json;
using Update;
using Update.UI;

namespace Squirrel.Update
{
  internal enum UpdateAction
  {
    Unset = 0,
    Install,
    Uninstall,
    Download,
    Update,
    Releasify,
    Shortcut,
    Deshortcut,
    ProcessStart,
    UpdateSelf,
    CheckForUpdate
  }

  internal partial class Program : IEnableLogger
  {
    #region Constants & Statics

    private static StartupOption opt;

    private static int consoleCreated = 0;

    #endregion




    #region Methods

    [STAThread]
    public static int Main(string[] args)
    {
      var pg = new Program();

      try
      {
        return pg.Run(args);
      }
      catch (Exception ex)
      {
        // NB: Normally this is a terrible idea but we want to make
        // sure Setup.exe above us gets the nonzero error code
        Console.Error.WriteLine(ex);
        return -1;
      }
    }

    private int Run(string[] args)
    {
      try
      {
        if (File.Exists("debugger"))
          Debugger.Launch();

        opt = new StartupOption(args);
      }
      catch (Exception ex)
      {
        using (var logger = new SetupLogLogger(true, "OptionParsing") { Level = LogLevel.Info })
        {
          Locator.CurrentMutable.Register(() => logger, typeof(Splat.ILogger));
          logger.Write($"Failed to parse command line options. {ex.Message}", LogLevel.Error);
        }

        throw;
      }

      // NB: Trying to delete the app directory while we have Setup.log
      // open will actually crash the uninstaller
      bool isUninstalling = opt.updateAction == UpdateAction.Uninstall;

      using (var logger = new SetupLogLogger(isUninstalling, opt.updateAction.ToString()) { Level = LogLevel.Info })
      {
        Locator.CurrentMutable.Register(() => logger, typeof(Splat.ILogger));

        try
        {
          return ExecuteCommandLine(args);
        }
        catch (Exception ex)
        {
          logger.Write("Finished with unhandled exception: " + ex, LogLevel.Fatal);
          throw;
        }
      }
    }

    private int ExecuteCommandLine(string[] args)
    {
      var animatedGifWindowToken = new CancellationTokenSource();

#if !MONO
      // Uncomment to test Gifs
      /*
      var ps = new ProgressSource();
      int i = 0; var t = new Timer(_ => ps.Raise(i += 10), null, 0, 1000);
      AnimatedGifWindow.ShowWindow(TimeSpan.FromMilliseconds(0), animatedGifWindowToken.Token, ps);
      Thread.Sleep(10 * 60 * 1000);
      */
#endif

      using (Disposable.Create(() => animatedGifWindowToken.Cancel()))
      {
        this.Log().Info("Starting Squirrel Updater: " + String.Join(" ", args));

        if (args.Any(x => x.StartsWith("/squirrel", StringComparison.OrdinalIgnoreCase))
          ) // NB: We're marked as Squirrel-aware, but we don't want to do
          // anything in response to these events
          return 0;

        switch (opt.updateAction)
        {
#if !MONO
          case UpdateAction.Install:
            var installLocation = Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
              Resources.PackageName
            );
            if (System.Windows.MessageBox.Show(
                $"This will install {Resources.AppTitle} on your computer. The install location is {installLocation}.\nDo you want to continue ?",
                Resources.AppTitle + " Installer", System.Windows.MessageBoxButton.YesNo)
              == System.Windows.MessageBoxResult.No)
              return 0;

            var progressSource = new ProgressSource();
            if (!opt.silentInstall)
              AnimatedGifWindow.ShowWindow(TimeSpan.FromSeconds(0), animatedGifWindowToken.Token, progressSource);

            Install(opt.silentInstall, progressSource, Path.GetFullPath(opt.target)).Wait();
            animatedGifWindowToken.Cancel();
            break;
          case UpdateAction.Uninstall:
            Uninstall().Wait();
            break;
          case UpdateAction.Download:
            Console.WriteLine(Download(opt.target).Result);
            break;
          case UpdateAction.Update:
            Update(opt.target).Wait();
            break;
          case UpdateAction.CheckForUpdate:
            Console.WriteLine(CheckForUpdate(opt.target).Result);
            break;
          case UpdateAction.UpdateSelf:
            UpdateSelf().Wait();
            break;
          case UpdateAction.Shortcut:
            Shortcut(opt.target, opt.shortcutArgs, opt.processStartArgs, opt.setupIcon);
            break;
          case UpdateAction.Deshortcut:
            Deshortcut(opt.target, opt.shortcutArgs);
            break;
          case UpdateAction.ProcessStart:
            ProcessStart(opt.processStart, opt.processStartArgs, opt.shouldWait);
            break;
          case UpdateAction.Unset:
            return ShowUpdater();
#endif
          case UpdateAction.Releasify:
            Releasify(opt.target, opt.releaseDir, opt.packagesDir, opt.bootstrapperExe, opt.backgroundGif, opt.signingParameters,
                      opt.baseUrl, opt.updateUrl, opt.setupIcon, !opt.noMsi, opt.packageAs64Bit, opt.frameworkVersion,
                      opt.exeStubRegexPattern, !opt.noDelta);
            break;
        }
      }

      this.Log().Info("Finished Squirrel Updater");
      return 0;
    }

    public void ShowHelp()
    {
      ensureConsole();
      opt.WriteOptionDescriptions();
    }

    private void waitForParentToExit()
    {
      // Grab a handle the parent process
      var parentPid = NativeMethods.GetParentProcessId();
      var handle    = default(IntPtr);

      // Wait for our parent to exit
      try
      {
        handle = NativeMethods.OpenProcess(ProcessAccess.Synchronize, false, parentPid);
        if (handle != IntPtr.Zero)
        {
          this.Log().Info("About to wait for parent PID {0}", parentPid);
          NativeMethods.WaitForSingleObject(handle, 0xFFFFFFFF /*INFINITE*/);
        }
        else
        {
          this.Log().Info("Parent PID {0} no longer valid - ignoring", parentPid);
        }
      }
      finally
      {
        if (handle != IntPtr.Zero) NativeMethods.CloseHandle(handle);
      }
    }

    private static string getAppNameFromDirectory(string path = null)
    {
      path = path ?? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
      return new DirectoryInfo(path).Name;
    }

    private static ShortcutLocation? parseShortcutLocations(string shortcutArgs)
    {
      var ret = default(ShortcutLocation?);

      if (!String.IsNullOrWhiteSpace(shortcutArgs))
      {
        var args = shortcutArgs.Split(new[] { ',' });

        foreach (var arg in args)
        {
          var location = (ShortcutLocation)Enum.Parse(typeof(ShortcutLocation), arg, false);
          if (ret.HasValue)
            ret |= location;
          else
            ret = location;
        }
      }

      return ret;
    }

    private static void ensureConsole()
    {
      if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;

      if (Interlocked.CompareExchange(ref consoleCreated, 1, 0) == 1) return;

      if (!NativeMethods.AttachConsole(-1))
        NativeMethods.AllocConsole();

      NativeMethods.GetStdHandle(StandardHandles.STD_ERROR_HANDLE);
      NativeMethods.GetStdHandle(StandardHandles.STD_OUTPUT_HANDLE);
    }

    #endregion
  }

  public class ProgressSource
  {
    #region Methods

    public void Raise(int i)
    {
      if (Progress != null)
        Progress.Invoke(this, i);
    }

    #endregion




    #region Events

    public event EventHandler<int> Progress;

    #endregion
  }

  internal class SetupLogLogger : Splat.ILogger, IDisposable
  {
    #region Properties & Fields - Non-Public

    private readonly object     gate = 42;
    private readonly TextWriter inner;

    #endregion




    #region Constructors

    public SetupLogLogger(bool saveInTemp, string commandSuffix = null)
    {
      for (int i = 0; i < 10; i++)
        try
        {
          var dir = saveInTemp ? Path.GetTempPath() : Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
          var fileName = commandSuffix == null
            ? String.Format($"Squirrel.{i}.log", i)
            : String.Format($"Squirrel-{commandSuffix}.{i}.log", i);
          var file = Path.Combine(dir, fileName.Replace(".0.log", ".log"));
          var str  = File.Open(file, FileMode.Append, FileAccess.Write, FileShare.Read);
          inner = new StreamWriter(str, Encoding.UTF8, 4096, false) { AutoFlush = true };
          return;
        }
        catch (Exception ex)
        {
          // Didn't work? Keep going
          Console.Error.WriteLine("Couldn't open log file, trying new file: " + ex.ToString());
        }

      inner = Console.Error;
    }

    public void Dispose()
    {
      lock (gate)
      {
        inner.Flush();
        inner.Dispose();
      }
    }

    #endregion




    #region Properties Impl - Public

    public LogLevel Level { get; set; }

    #endregion




    #region Methods Impl

    public void Write(string message, LogLevel logLevel)
    {
      if (logLevel < Level)
        return;

      lock (gate) inner.WriteLine($"[{DateTime.Now.ToString("dd/MM/yy HH:mm:ss")}] {logLevel.ToString().ToLower()}: {message}");
    }

    #endregion
  }
}
