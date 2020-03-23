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
// Modified On:  2020/03/19 12:38
// Modified By:  Alexis

#endregion




using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Splat;

namespace Squirrel
{
  public sealed partial class UpdateManager
  {
    internal class DownloadReleasesImpl : IEnableLogger
    {
      #region Properties & Fields - Non-Public

      private readonly string rootAppDirectory;

      #endregion




      #region Constructors

      public DownloadReleasesImpl(string rootAppDirectory)
      {
        this.rootAppDirectory = rootAppDirectory;
      }

      #endregion




      #region Methods

      public async Task DownloadReleases(string                    updateUrlOrPath,
                                         IEnumerable<ReleaseEntry> releasesToDownload,
                                         Action<int>               progress      = null,
                                         IFileDownloader           urlDownloader = null)
      {
        progress      = progress ?? (_ => { });
        urlDownloader = urlDownloader ?? new FileDownloader();
        var packagesDirectory = Path.Combine(rootAppDirectory, "packages");

        double current     = 0;
        double toIncrement = 100.0 / releasesToDownload.Count();

        if (Utility.IsHttpUrl(updateUrlOrPath)) // From Internet
          await releasesToDownload.ForEachAsync(async x =>
          {
            var    targetFile = Path.Combine(packagesDirectory, x.Filename);
            double component  = 0;
            await DownloadRelease(updateUrlOrPath, x, urlDownloader, targetFile, p =>
            {
              lock (progress)
              {
                current   -= component;
                component =  toIncrement / 100.0 * p;
                progress((int)Math.Round(current += component));
              }
            }).ConfigureAwait(false);

            ChecksumPackage(x);
          }).ConfigureAwait(false);
        else // From Disk
          await releasesToDownload.ForEachAsync(x =>
          {
            var targetFile = Path.Combine(packagesDirectory, x.Filename);

            File.Copy(
              Path.Combine(updateUrlOrPath, x.Filename),
              targetFile,
              true);

            lock (progress) progress((int)Math.Round(current += toIncrement));
            ChecksumPackage(x);
          }).ConfigureAwait(false);
      }

      private bool IsReleaseExplicitlyHttp(ReleaseEntry x)
      {
        return x.BaseUrl != null &&
          Uri.IsWellFormedUriString(x.BaseUrl, UriKind.Absolute);
      }

      private async Task DownloadRelease(string          updateBaseUrl,
                                   ReleaseEntry    releaseEntry,
                                   IFileDownloader urlDownloader,
                                   string          targetFile,
                                   Action<int>     progress)
      {
        var baseUri         = Utility.EnsureTrailingSlash(new Uri(updateBaseUrl));
        var releaseEntryUrl = releaseEntry.BaseUrl + releaseEntry.Filename;

        if (!String.IsNullOrEmpty(releaseEntry.Query))
          releaseEntryUrl += releaseEntry.Query;

        var sourceFileUrl = new Uri(baseUri, releaseEntryUrl).AbsoluteUri;
        File.Delete(targetFile);
        try
        {
          await urlDownloader.DownloadFile(sourceFileUrl, targetFile, progress).ConfigureAwait(false);
        }
        catch (WebException ex)
        {
          this.Log().InfoException($"Download resulted in WebException while downloading '{sourceFileUrl}' (returning blank release list)",
                                   ex);
          throw;
        }
      }

      private Task ChecksumAllPackages(IEnumerable<ReleaseEntry> releasesDownloaded)
      {
        return releasesDownloaded.ForEachAsync(x => ChecksumPackage(x));
      }

      private void ChecksumPackage(ReleaseEntry downloadedRelease)
      {
        var targetPackage = new FileInfo(
          Path.Combine(rootAppDirectory, "packages", downloadedRelease.Filename));

        if (!targetPackage.Exists)
        {
          this.Log().Error("File {0} should exist but doesn't", targetPackage.FullName);

          throw new Exception("Checksummed file doesn't exist: " + targetPackage.FullName);
        }

        if (targetPackage.Length != downloadedRelease.Filesize)
        {
          this.Log().Error("File Length should be {0}, is {1}", downloadedRelease.Filesize, targetPackage.Length);
          targetPackage.Delete();

          throw new Exception("Checksummed file size doesn't match: " + targetPackage.FullName);
        }

        using (var file = targetPackage.OpenRead())
        {
          var hash = Utility.CalculateStreamSHA1(file);

          if (!hash.Equals(downloadedRelease.SHA1, StringComparison.OrdinalIgnoreCase))
          {
            this.Log().Error("File SHA1 should be {0}, is {1}", downloadedRelease.SHA1, hash);
            targetPackage.Delete();
            throw new Exception("Checksum doesn't match: " + targetPackage.FullName);
          }
        }
      }

      #endregion
    }
  }
}
