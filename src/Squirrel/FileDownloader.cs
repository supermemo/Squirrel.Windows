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
// Modified On:  2020/03/19 12:31
// Modified By:  Alexis

#endregion




using System;
using System.Net;
using System.Threading.Tasks;
using Splat;

namespace Squirrel
{
  public interface IFileDownloader
  {
    Task         DownloadFile(string url, string targetFile, Action<int> progress);
    Task<byte[]> DownloadUrl(string  url);
  }

  public class FileDownloader : IFileDownloader, IEnableLogger
  {
    #region Properties & Fields - Non-Public

    private readonly WebClient _providedClient;

    #endregion




    #region Constructors

    public FileDownloader(WebClient providedClient = null)
    {
      _providedClient = providedClient;
    }

    #endregion




    #region Methods Impl

    public async Task<byte[]> DownloadUrl(string url)
    {
      using (var wc = _providedClient ?? Utility.CreateWebClient())
      {
        var failedUrl = default(string);

        while (true)
        {
          try
          {
            this.Log().Info("Downloading url: " + (failedUrl ?? url));

            return await this.WarnIfThrows(() => wc.DownloadDataTaskAsync(failedUrl ?? url),
                                           "Failed to download url: " + (failedUrl ?? url));
          }
          catch (Exception)
          {
            // NB: Some super brain-dead services are case-sensitive yet 
            // corrupt case on upload. I can't even.
            if (failedUrl != null) throw;

            failedUrl = url.ToLower();
          }
        }
      }
    }

    #endregion




    #region Methods

    public async Task DownloadFile(string url, string targetFile, Action<int> progress)
    {
      using (var wc = _providedClient ?? Utility.CreateWebClient())
      {
        var failedUrl = default(string);
        var lastSignaled = DateTime.MinValue;

        wc.DownloadProgressChanged += (sender, args) =>
        {
          var now = DateTime.Now;

          if (now - lastSignaled > TimeSpan.FromMilliseconds(500))
          {
            lastSignaled = now;
            progress(args.ProgressPercentage);
          }
        };

        while (true)
          try
          {
            this.Log().Info("Downloading file: " + (failedUrl ?? url));

            await this.WarnIfThrows(
              async () =>
              {
                await wc.DownloadFileTaskAsync(failedUrl ?? url, targetFile).ConfigureAwait(false);
                progress(100);
              },
              "Failed downloading URL: " + (failedUrl ?? url)
            ).ConfigureAwait(false);

            break;
          }
          catch (Exception)
          {
            // NB: Some super brain-dead services are case-sensitive yet 
            // corrupt case on upload. I can't even.
            if (failedUrl != null) throw;

            failedUrl = url.ToLower();
            progress(0);
          }
      }
    }

    #endregion
  }
}
