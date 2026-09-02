using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RelationshipGraphNative
{
    /// <summary>
    /// Stores a small most-recently-used file list. Public methods are safe to
    /// call concurrently on one instance and never surface persistence errors.
    /// </summary>
    internal sealed class RecentFileStore
    {
        internal const int MaximumFiles = 10;
        private const int MaximumLinesToInspect = 256;
        private readonly object _sync = new object();
        private readonly string _storePath;
        private Exception _lastError;

        public RecentFileStore()
            : this(DefaultStorePath())
        {
        }

        public RecentFileStore(string storePath)
        {
            if (String.IsNullOrWhiteSpace(storePath)) throw new ArgumentException("最近文件存储路径不能为空。", "storePath");
            _storePath = Path.GetFullPath(storePath);
        }

        public string StorePath { get { return _storePath; } }

        /// <summary>
        /// Last recoverable path or I/O error. A successful operation clears it.
        /// </summary>
        public Exception LastError
        {
            get { lock (_sync) return _lastError; }
        }

        /// <summary>
        /// Returns newest-first existing files. Invalid, duplicate and missing
        /// entries are ignored and, when possible, compacted on disk.
        /// </summary>
        public IList<string> GetFiles()
        {
            lock (_sync)
            {
                bool needsRewrite;
                Exception readError;
                List<string> files = ReadCore(out needsRewrite, out readError);
                if (readError != null)
                {
                    _lastError = readError;
                    return files.AsReadOnly();
                }

                _lastError = null;
                if (needsRewrite)
                {
                    Exception writeError;
                    if (!TryWriteCore(files, out writeError)) _lastError = writeError;
                }
                return files.AsReadOnly();
            }
        }

        /// <summary>
        /// Adds an existing file at the front of the list. Returns false for an
        /// invalid/missing path or when the atomic persistence step fails.
        /// </summary>
        public bool Add(string fileName)
        {
            string normalized;
            Exception pathError;
            if (!TryNormalizePath(fileName, true, out normalized, out pathError))
            {
                lock (_sync) _lastError = pathError;
                return false;
            }

            lock (_sync)
            {
                bool needsRewrite;
                Exception readError;
                List<string> files = ReadCore(out needsRewrite, out readError);
                if (readError != null) { _lastError = readError; return false; }

                files.RemoveAll(delegate(string item) { return PathsEqual(item, normalized); });
                files.Insert(0, normalized);
                if (files.Count > MaximumFiles) files.RemoveRange(MaximumFiles, files.Count - MaximumFiles);

                Exception writeError;
                bool saved = TryWriteCore(files, out writeError);
                _lastError = writeError;
                return saved;
            }
        }

        /// <summary>
        /// Removes a path from the list. The target file does not need to exist.
        /// A successful no-op also returns true.
        /// </summary>
        public bool Remove(string fileName)
        {
            string normalized;
            Exception pathError;
            if (!TryNormalizePath(fileName, false, out normalized, out pathError))
            {
                lock (_sync) _lastError = pathError;
                return false;
            }

            lock (_sync)
            {
                bool needsRewrite;
                Exception readError;
                List<string> files = ReadCore(out needsRewrite, out readError);
                if (readError != null) { _lastError = readError; return false; }

                files.RemoveAll(delegate(string item) { return PathsEqual(item, normalized); });
                Exception writeError;
                bool saved = TryWriteCore(files, out writeError);
                _lastError = writeError;
                return saved;
            }
        }

        /// <summary>
        /// Atomically replaces the store with an empty list.
        /// </summary>
        public bool Clear()
        {
            lock (_sync)
            {
                Exception writeError;
                bool saved = TryWriteCore(new List<string>(), out writeError);
                _lastError = writeError;
                return saved;
            }
        }

        private List<string> ReadCore(out bool needsRewrite, out Exception error)
        {
            needsRewrite = false;
            error = null;
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (!File.Exists(_storePath)) return result;
                using (StreamReader reader = new StreamReader(_storePath, Encoding.UTF8, true))
                {
                    int inspected = 0;
                    while (!reader.EndOfStream && inspected < MaximumLinesToInspect)
                    {
                        string line = reader.ReadLine();
                        inspected++;
                        if (String.IsNullOrWhiteSpace(line)) { needsRewrite = true; continue; }

                        string normalized;
                        Exception pathError;
                        if (!TryNormalizePath(line, true, out normalized, out pathError))
                        {
                            needsRewrite = true;
                            continue;
                        }
                        if (!seen.Add(normalized)) { needsRewrite = true; continue; }

                        result.Add(normalized);
                        if (result.Count == MaximumFiles)
                        {
                            if (!reader.EndOfStream) needsRewrite = true;
                            break;
                        }
                    }
                    if (!reader.EndOfStream) needsRewrite = true;
                }
            }
            catch (Exception readError)
            {
                error = readError;
                needsRewrite = false;
                result.Clear();
            }
            return result;
        }

        private bool TryWriteCore(IList<string> files, out Exception error)
        {
            error = null;
            try
            {
                StringBuilder content = new StringBuilder();
                int count = Math.Min(MaximumFiles, files == null ? 0 : files.Count);
                for (int index = 0; index < count; index++)
                {
                    if (String.IsNullOrWhiteSpace(files[index])) continue;
                    content.Append(files[index]);
                    content.Append(Environment.NewLine);
                }
                NativePersistence.WriteAllTextAtomic(_storePath, content.ToString(), new UTF8Encoding(false), false);
                return true;
            }
            catch (Exception writeError)
            {
                error = writeError;
                return false;
            }
        }

        private static bool TryNormalizePath(string fileName, bool requireExisting, out string normalized, out Exception error)
        {
            normalized = "";
            error = null;
            try
            {
                if (String.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("文件路径不能为空。", "fileName");
                normalized = Path.GetFullPath(fileName);
                if (requireExisting && !File.Exists(normalized)) throw new FileNotFoundException("最近文件已不存在。", normalized);
                return true;
            }
            catch (Exception pathError)
            {
                error = pathError;
                normalized = "";
                return false;
            }
        }

        private static bool PathsEqual(string first, string second)
        {
            return String.Equals(first ?? "", second ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private static string DefaultStorePath()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "Relationship Studio", "recent-files.txt");
        }
    }
}
