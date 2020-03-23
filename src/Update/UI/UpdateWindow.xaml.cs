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
// Modified On:  2020/03/21 21:50
// Modified By:  Alexis

#endregion




using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using NuGet;
using Splat;
using Squirrel;
using Update.Models;
using Update.Sys.Windows;

namespace Update.UI
{
  /// <summary>Interaction logic for UpdateWindow.xaml</summary>
  public partial class UpdateWindow : Window, INotifyPropertyChanged, IEnableLogger
  {
    #region Properties & Fields - Non-Public

    private readonly string                 _urlOrPath;
    private          RemoteAndLocalReleases _allReleases;

    #endregion




    #region Constructors

    public UpdateWindow(string urlOrPath)
    {
      _urlOrPath = urlOrPath;

      ReleaseEntries = new ObservableCollection<ReleaseEntryViewModel>();
      InstallCommand = new AsyncRelayCommand<ReleaseEntryViewModel>(Install, CanInstall);

      Title  = Update.Resources.AppTitle + " Manual Updater";
      Status = "Idle";

      InitializeComponent();

      Dispatcher.InvokeAsync(async () => await RefreshReleases());
    }

    #endregion




    #region Properties & Fields - Public

    public ObservableCollection<ReleaseEntryViewModel> ReleaseEntries        { get; }
    public IAsyncCommand<ReleaseEntryViewModel>        InstallCommand        { get; }
    public int                                         ProgressValue         { get; private set; }
    public bool                                        ProgressIndeterminate { get; private set; }
    public bool                                        CanExecuteCommand     { get; private set; } = true;
    public string                                      Status                { get; private set; }

    #endregion




    #region Methods

    private UpdateManager CreateMgr() => new UpdateManager(
      _urlOrPath,
      Update.Resources.PackageName,
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    private UpdaterIntention GetIntention(UpdateManager updateManager) =>
      updateManager.IsInstalledApp
        ? UpdaterIntention.Update
        : UpdaterIntention.Install;

    private void UpdateProgress(int progress)
    {
      Dispatcher.Invoke(() => ProgressValue = progress);
    }

    private async Task RefreshReleases(UpdateManager updateMgr = null)
    {
      bool ShouldAddEntry(ReleaseEntry re, HashSet<SemanticVersion> addedVersions)
      {
        if (addedVersions.Contains(re.Version) == false)
        {
          addedVersions.Add(re.Version);
          return true;
        }

        return false;
      }

      try
      {
        Status                = "Refreshing";
        CanExecuteCommand     = false;
        ProgressIndeterminate = true;

        IDisposable dispMgr = null;

        if (updateMgr == null)
          dispMgr = updateMgr = CreateMgr();

        try
        {
          _allReleases = await updateMgr.FetchAllReleases(UpdateProgress)
                                        .ConfigureAwait(true);

          if (_allReleases == null || _allReleases.Remote?.Any() == false)
            return;

          var addedVersions = new HashSet<SemanticVersion>();

          var releaseEntries =
            _allReleases.Remote?
                        .OrderBy(r => r.Version)
                        .Where(r => ShouldAddEntry(r, addedVersions))
                        .ToList()
                        .Select(r => new ReleaseEntryViewModel(r, r.Version == _allReleases.CurrentRelease?.Version));

          ReleaseEntries.Clear();

          if ((releaseEntries?.Any() ?? false) == false)
            return;

          ReleaseEntries.AddRange(releaseEntries);
        }
        finally
        {
          dispMgr?.Dispose();
        }
      }
      catch (Exception ex)
      {
        MessageBox.Show($"An error occured while fetching releases: {ex.Message}");
      }
      finally
      {
        CanExecuteCommand     = true;
        ProgressIndeterminate = false;
        Status = "Idle";
      }
    }

    private async Task Install(ReleaseEntryViewModel releaseEntry)
    {
      try
      {
        using (var updateMgr = CreateMgr())
        {
          Status = $"Calculating update path to version {releaseEntry.ReleaseEntry.Version}";

          var updatePath = updateMgr.CalculateUpdatePath(
            _allReleases,
            releaseEntry.ReleaseEntry,
            true,
            false,
            GetIntention(updateMgr));

          UpdateProgress(10);
          
          Status = $"Downloading version {releaseEntry.ReleaseEntry.Version}";

          await updateMgr.DownloadReleases(updatePath.ReleasesToApply, p => UpdateProgress(10 + (int)(p * .40)))
                         .ConfigureAwait(true);
          
          Status = $"Installing version {releaseEntry.ReleaseEntry.Version}";

          await updateMgr.ApplyReleases(updatePath, p => UpdateProgress(50 + (int)(p * .50)))
                         .ConfigureAwait(true);

          var oldCurrentRelease = ReleaseEntries.FirstOrDefault(r => r.IsCurrent);

          oldCurrentRelease.IsCurrent = false;
          releaseEntry.IsCurrent      = true;

          MessageBox.Show($"Successfully installed version {releaseEntry.ReleaseEntry.Version}");
        }
      }
      catch (AggregateException aggEx)
      {
        MessageBox.Show($"An error occured while installing: {aggEx.InnerException.Message}");

        this.Log().Error($"Failed to install version {releaseEntry.ReleaseEntry.Version}", aggEx);
      }
      catch (Exception ex)
      {
        MessageBox.Show($"An error occured while installing: {ex.Message}");

        this.Log().Error($"Failed to install version {releaseEntry.ReleaseEntry.Version}", ex);
      }
      finally
      {
        Status        = "Idle";
        ProgressValue = 0;

        await RefreshReleases().ConfigureAwait(false);
      }
    }

    private bool CanInstall(ReleaseEntryViewModel releaseEntry)
    {
      return releaseEntry != null && releaseEntry.IsCurrent == false;
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
      if (CanExecuteCommand == false || InstallCommand.IsExecuting)
        e.Cancel = true;
    }

    #endregion




    #region Events

    /// <inheritdoc />
    public event PropertyChangedEventHandler PropertyChanged;

    #endregion
  }
}
