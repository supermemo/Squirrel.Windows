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
// Modified On:  2020/03/20 15:00
// Modified By:  Alexis

#endregion




using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.Serialization;
using NuGet;
using Splat;
using Squirrel.Extensions;
using EnumerableExtensions = Squirrel.Extensions.EnumerableExtensions;

namespace Squirrel
{
  [DataContract]
  public class UpdateInfo : IEnableLogger
  {
    #region Constructors

    protected UpdateInfo(ReleaseEntry currentlyInstalledVersion, IEnumerable<ReleaseEntry> releasesToApply, string packageDirectory)
    {
      // NB: When bootstrapping, CurrentlyInstalledVersion is null!
      CurrentlyInstalledVersion = currentlyInstalledVersion;
      ReleasesToApply           = (releasesToApply ?? Enumerable.Empty<ReleaseEntry>()).ToList();
      FutureReleaseEntry =
        ReleasesToApply.Any() ? ReleasesToApply.MaxBy(x => x.Version).FirstOrDefault() : CurrentlyInstalledVersion;

      PackageDirectory = packageDirectory;
    }

    #endregion




    #region Properties & Fields - Public

    [DataMember]
    public ReleaseEntry CurrentlyInstalledVersion { get; protected set; }
    [DataMember]
    public ReleaseEntry FutureReleaseEntry { get; protected set; }
    [DataMember]
    public List<ReleaseEntry> ReleasesToApply { get; protected set; }

    [IgnoreDataMember]
    public bool IsBootstrapping
    {
      get { return CurrentlyInstalledVersion == null; }
    }

    [IgnoreDataMember]
    public string PackageDirectory { get; protected set; }

    #endregion




    #region Methods

    public Dictionary<ReleaseEntry, string> FetchReleaseNotes()
    {
      return ReleasesToApply
             .SelectMany(x =>
             {
               try
               {
                 var releaseNotes = x.GetReleaseNotes(PackageDirectory);
                 return EnumerableExtensions.Return(Tuple.Create(x, releaseNotes));
               }
               catch (Exception ex)
               {
                 this.Log().WarnException("Couldn't get release notes for:" + x.Filename, ex);
                 return Enumerable.Empty<Tuple<ReleaseEntry, string>>();
               }
             })
             .ToDictionary(k => k.Item1, v => v.Item2);
    }

    public static UpdateInfo Create(
      ReleaseEntry          currentVersion,
      ReleaseEntry          targetVersion,
      HashSet<ReleaseEntry> availableReleases,
      string                packageDirectory,
      bool                  allowDowngrade)
    {
      Contract.Requires(availableReleases != null);
      Contract.Requires(!String.IsNullOrEmpty(packageDirectory));

      if (targetVersion == null)
        throw new ArgumentNullException(nameof(targetVersion));

      if (availableReleases == null)
        throw new ArgumentNullException(nameof(availableReleases));

      if (availableReleases.Any() == false)
        throw new ArgumentException("UpdateInfo::Create: Empty remote releases");

      if (availableReleases.Select(r => r.Version).Contains(targetVersion.Version) == false)
        throw new ArgumentException($"Trying to update to a non-existent remote version: {targetVersion.Version}");

      //
      // Eliminate same version calls
      if (currentVersion != null && targetVersion.Version.Equals(currentVersion.Version))
        return new UpdateInfo(currentVersion, Enumerable.Empty<ReleaseEntry>(), packageDirectory);

      //
      // Compute path
      List<ReleaseEntry> updatePath;

      // Downgrades
      bool isDowngrade = currentVersion != null && targetVersion.Version < currentVersion.Version;

      if (isDowngrade)
      {
        if (allowDowngrade == false)
          throw new ArgumentException(
            $"Trying to downgrade from version {currentVersion.Version} to version {targetVersion.Version} with downgrade option disabled");

        var allUntilTarget = availableReleases.Where(r => r.Version <= targetVersion.Version)
                                              .OrderByDescending(r => r.Version)
                                              .ToList();
        var nearestFullPkg = allUntilTarget.FirstOrDefault(r => r.IsDelta == false);

        if (nearestFullPkg == null)
          throw new ArgumentException(
            $"Trying to downgrade from version {currentVersion.Version} to version {targetVersion.Version}, but no full package for or before target version exist");

        updatePath = CreateDirectPath(nearestFullPkg, allUntilTarget, targetVersion);
      }

      // Upgrades or install
      else
      {
        var allUpdatePkg = (currentVersion != null
                             ? availableReleases.Where(r => r.Version > currentVersion.Version && r.Version <= targetVersion.Version)
                             : availableReleases.Where(r => r.Version <= targetVersion.Version))
                           .OrderByDescending(r => r.Version)
                           .ToList();
        var nearestFullPkg = allUpdatePkg.FirstOrDefault(r => r.IsDelta == false);

        // New install
        if (currentVersion == null)
        {
          if (nearestFullPkg == null)
            throw new ArgumentException(
              $"Trying to install version {targetVersion.Version}, but no full package for or before target version exist");

          updatePath = CreateDirectPath(nearestFullPkg, allUpdatePkg, targetVersion);
        }

        // Upgrade from current version
        else if (nearestFullPkg == null)
        {
          updatePath = CreateDirectPath(currentVersion, allUpdatePkg, targetVersion, false);
        }

        // Determine whether to upgrade from current version of nearest full pkg
        else
        {
          // Make sure the delta path exists (e.g. only the full package might have been deployed for certain versions)
          var verDeltaMap = allUpdatePkg.Aggregate(
            new Dictionary<SemanticVersion, bool>(),
            (dict, r) =>
            {
              dict[r.Version] = r.IsDelta || dict.SafeGet(r.Version, false);

              return dict;
            });

          if (verDeltaMap.Values.Any(hasDelta => hasDelta == false))
          {
            updatePath = CreateDirectPath(nearestFullPkg, allUpdatePkg, targetVersion, false);
          }

          else
          {
            var deltaUpdatePath  = CreateDirectPath(currentVersion, allUpdatePkg, targetVersion, false);
            var directUpdatePath = CreateDirectPath(nearestFullPkg, allUpdatePkg, targetVersion, true);

            var deltasSize = deltaUpdatePath.Sum(r => r.Filesize);
            var directSize = directUpdatePath.Sum(r => r.Filesize);

            updatePath = deltasSize < directSize
              ? deltaUpdatePath
              : directUpdatePath;
          }
        }
      }

      return new UpdateInfo(currentVersion, updatePath.OrderBy(r => r.Version), packageDirectory);
    }

    private static List<ReleaseEntry> CreateDirectPath(ReleaseEntry              nearestFullPkg,
                                                       IEnumerable<ReleaseEntry> allUntilTarget,
                                                       ReleaseEntry              targetVersion,
                                                       bool                      includeNearestFullPkg = true)
    {
      List<ReleaseEntry> updatePath = new List<ReleaseEntry>();

      if (nearestFullPkg.Version.Equals(targetVersion.Version))
      {
        updatePath.Add(nearestFullPkg);
        return updatePath;
      }

      if (includeNearestFullPkg)
        updatePath.Add(nearestFullPkg);

      updatePath.AddRange(allUntilTarget.Where(r => r.IsDelta && r.Version > nearestFullPkg.Version && r.Version <= targetVersion.Version));

      return updatePath;
    }

    #endregion
  }
}
