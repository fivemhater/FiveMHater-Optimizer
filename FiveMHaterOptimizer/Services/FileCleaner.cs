using System;
using System.Collections.Generic;
using System.IO;

namespace FiveMHaterOptimizer.Services;

/// <summary>
/// Güvenli dosya temizliği: bağlantı noktalarını (junction/symlink) ASLA takip etmez, kilitli dosyaları atlar,
/// salt okunur dosyaları yalnızca hedef klasörlerde siler.
/// </summary>
public static class FileCleaner
{
    public sealed class CleanResult
    {
        public long BytesFreed { get; set; }
        public int FilesDeleted { get; set; }
        public int FilesSkipped { get; set; }
        public double MB => BytesFreed / 1024d / 1024d;
    }

    public static CleanResult CleanDirectory(string root, TimeSpan? minAge = null, Func<FileInfo, bool>? filter = null,
        bool removeEmptyDirs = true, ISet<string>? preserveTopLevelDirs = null)
    {
        var result = new CleanResult();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return result;

        DirectoryInfo rootInfo;
        try { rootInfo = new DirectoryInfo(root); }
        catch { return result; }
        if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0) return result;

        DateTime cutoff = minAge.HasValue ? DateTime.Now - minAge.Value : DateTime.MaxValue;
        var stack = new Stack<(DirectoryInfo dir, int depth)>();
        var visitedDirs = new List<DirectoryInfo>();
        stack.Push((rootInfo, 0));

        while (stack.Count > 0)
        {
            var (dir, depth) = stack.Pop();
            FileSystemInfo[] entries;
            try { entries = dir.GetFileSystemInfos(); }
            catch { continue; }

            foreach (var entry in entries)
            {
                try
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue; // junction/symlink: dokunma
                    if (entry is DirectoryInfo sub)
                    {
                        if (depth == 0 && preserveTopLevelDirs != null && preserveTopLevelDirs.Contains(sub.Name)) continue;
                        stack.Push((sub, depth + 1));
                        visitedDirs.Add(sub);
                        continue;
                    }
                    if (entry is not FileInfo file) continue;
                    if (minAge.HasValue && file.LastWriteTime > cutoff) continue;
                    if (filter != null && !filter(file)) continue;

                    long len = file.Length;
                    if ((file.Attributes & FileAttributes.ReadOnly) != 0) file.Attributes &= ~FileAttributes.ReadOnly;
                    file.Delete();
                    result.BytesFreed += len;
                    result.FilesDeleted++;
                }
                catch
                {
                    result.FilesSkipped++; // kullanımda/kilitli: hata vermeden atla
                }
            }
        }

        if (removeEmptyDirs)
        {
            visitedDirs.Sort((a, b) => b.FullName.Length.CompareTo(a.FullName.Length));
            foreach (var d in visitedDirs)
            {
                try
                {
                    if (Directory.Exists(d.FullName) && Directory.GetFileSystemEntries(d.FullName).Length == 0)
                        Directory.Delete(d.FullName, false);
                }
                catch { }
            }
        }
        return result;
    }

    public static long DirectorySize(string root)
    {
        long size = 0;
        if (!Directory.Exists(root)) return 0;
        try
        {
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (string f in Directory.EnumerateFiles(root, "*", opts))
            {
                try { size += new FileInfo(f).Length; } catch { }
            }
        }
        catch { }
        return size;
    }
}
