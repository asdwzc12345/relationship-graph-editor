using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RelationshipGraphNative
{
    internal static class NativePersistence
    {
        public static void WriteAllTextAtomic(string fileName, string content, Encoding encoding, bool keepBackup)
        {
            if (encoding == null) throw new ArgumentNullException("encoding");
            WriteAllBytesAtomic(fileName, encoding.GetBytes(content ?? ""), keepBackup);
        }

        public static void WriteAllBytesAtomic(string fileName, byte[] content, bool keepBackup)
        {
            if (content == null) throw new ArgumentNullException("content");
            WriteStreamAtomic(fileName, keepBackup, delegate(Stream stream) { stream.Write(content, 0, content.Length); });
        }

        public static void WriteStreamAtomic(string fileName, bool keepBackup, Action<Stream> writer)
        {
            if (String.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("文件路径不能为空。", "fileName");
            if (writer == null) throw new ArgumentNullException("writer");

            string targetPath = Path.GetFullPath(fileName);
            string folder = Path.GetDirectoryName(targetPath);
            if (String.IsNullOrEmpty(folder)) throw new IOException("无法确定文件所在目录。");
            Directory.CreateDirectory(folder);

            string temporaryPath = TemporaryPathFor(targetPath);
            try
            {
                WriteNewFile(temporaryPath, writer);
                if (keepBackup && File.Exists(targetPath)) CopyFileAtomically(targetPath, targetPath + ".bak");
                PublishTemporaryFile(temporaryPath, targetPath);
                temporaryPath = null;
            }
            finally
            {
                DeleteTemporaryFile(temporaryPath);
            }
        }

        private static void WriteNewFile(string fileName, Action<Stream> writer)
        {
            using (FileStream stream = new FileStream(fileName, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                writer(stream);
                stream.Flush(true);
            }
        }

        private static void CopyFileAtomically(string sourcePath, string targetPath)
        {
            string temporaryPath = TemporaryPathFor(targetPath);
            try
            {
                using (FileStream source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (FileStream destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
                {
                    source.CopyTo(destination);
                    destination.Flush(true);
                }
                PublishTemporaryFile(temporaryPath, targetPath);
                temporaryPath = null;
            }
            finally
            {
                DeleteTemporaryFile(temporaryPath);
            }
        }

        private static void PublishTemporaryFile(string temporaryPath, string targetPath)
        {
            if (File.Exists(targetPath)) File.Replace(temporaryPath, targetPath, null, true);
            else File.Move(temporaryPath, targetPath);
        }

        private static string TemporaryPathFor(string targetPath)
        {
            string folder = Path.GetDirectoryName(targetPath);
            string name = Path.GetFileName(targetPath);
            return Path.Combine(folder, "." + name + "." + Guid.NewGuid().ToString("N") + ".tmp");
        }

        private static void DeleteTemporaryFile(string temporaryPath)
        {
            if (String.IsNullOrEmpty(temporaryPath)) return;
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch { }
        }
    }

    internal sealed class AutosaveRecoveryCandidate
    {
        public readonly string FileName;
        public readonly string Description;

        public AutosaveRecoveryCandidate(string fileName, string description)
        {
            FileName = fileName;
            Description = description;
        }
    }

    internal sealed class AutosaveStore
    {
        internal const int MaximumHistoryFiles = 10;
        internal static readonly TimeSpan HistoryInterval = TimeSpan.FromMinutes(5);
        internal static readonly TimeSpan HistoryRetryInterval = TimeSpan.FromSeconds(30);

        private readonly string _primaryPath;
        private readonly string _historyDirectory;
        private DateTime _lastHistoryAttemptUtc = DateTime.MinValue;

        public AutosaveStore(string primaryPath)
        {
            if (String.IsNullOrWhiteSpace(primaryPath)) throw new ArgumentException("自动保存路径不能为空。", "primaryPath");
            _primaryPath = Path.GetFullPath(primaryPath);
            string folder = Path.GetDirectoryName(_primaryPath);
            _historyDirectory = Path.Combine(folder, Path.GetFileNameWithoutExtension(_primaryPath) + ".history");
        }

        public string PrimaryPath { get { return _primaryPath; } }
        public string BackupPath { get { return _primaryPath + ".bak"; } }
        public string HistoryDirectory { get { return _historyDirectory; } }

        public string Save(string json)
        {
            NativePersistence.WriteAllTextAtomic(_primaryPath, json, new UTF8Encoding(false), true);

            DateTime now = DateTime.UtcNow;
            try
            {
                if (HistorySnapshotIsDue(now))
                {
                    try
                    {
                        SaveHistorySnapshot(json, now);
                        _lastHistoryAttemptUtc = now;
                    }
                    catch
                    {
                        _lastHistoryAttemptUtc = now - HistoryInterval + HistoryRetryInterval;
                        throw;
                    }
                }
                return "";
            }
            catch (Exception error)
            {
                return error.Message;
            }
        }

        public IList<AutosaveRecoveryCandidate> GetRecoveryCandidates()
        {
            List<AutosaveRecoveryCandidate> candidates = new List<AutosaveRecoveryCandidate>();
            if (File.Exists(_primaryPath)) candidates.Add(new AutosaveRecoveryCandidate(_primaryPath, "主自动恢复文件"));
            if (File.Exists(BackupPath)) candidates.Add(new AutosaveRecoveryCandidate(BackupPath, "自动恢复备份"));
            if (Directory.Exists(_historyDirectory))
            {
                foreach (FileInfo file in HistoryFilesNewestFirst())
                    candidates.Add(new AutosaveRecoveryCandidate(file.FullName, "历史恢复版本"));
            }
            return candidates;
        }

        public void ClearAll()
        {
            if (Directory.Exists(_historyDirectory))
            {
                foreach (FileInfo file in HistoryFilesNewestFirst()) file.Delete();
                if (!Directory.EnumerateFileSystemEntries(_historyDirectory).Any()) Directory.Delete(_historyDirectory, false);
            }
            if (File.Exists(BackupPath)) File.Delete(BackupPath);
            if (File.Exists(_primaryPath)) File.Delete(_primaryPath);
            _lastHistoryAttemptUtc = DateTime.MinValue;
        }

        private bool HistorySnapshotIsDue(DateTime now)
        {
            if (now - _lastHistoryAttemptUtc < HistoryInterval) return false;
            if (!Directory.Exists(_historyDirectory)) return true;
            FileInfo newest = HistoryFilesNewestFirst().FirstOrDefault();
            return newest == null || now - newest.LastWriteTimeUtc >= HistoryInterval;
        }

        private void SaveHistorySnapshot(string json, DateTime now)
        {
            Directory.CreateDirectory(_historyDirectory);
            string name = "autosave-" + now.ToString("yyyyMMdd-HHmmss-fff") + ".json";
            string historyPath = Path.Combine(_historyDirectory, name);
            if (File.Exists(historyPath))
                historyPath = Path.Combine(_historyDirectory, "autosave-" + now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".json");
            NativePersistence.WriteAllTextAtomic(historyPath, json, new UTF8Encoding(false), false);

            FileInfo[] files = HistoryFilesNewestFirst().ToArray();
            for (int index = MaximumHistoryFiles; index < files.Length; index++) files[index].Delete();
        }

        private IOrderedEnumerable<FileInfo> HistoryFilesNewestFirst()
        {
            DirectoryInfo folder = new DirectoryInfo(_historyDirectory);
            return folder.GetFiles("autosave-*.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(delegate(FileInfo file) { return file.LastWriteTimeUtc; })
                .ThenByDescending(delegate(FileInfo file) { return file.Name; });
        }
    }
}
