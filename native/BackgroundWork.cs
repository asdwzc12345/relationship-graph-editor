using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace RelationshipGraphNative
{
    internal sealed class BackgroundWorkProgressEventArgs : EventArgs
    {
        public long WorkId { get; private set; }
        public string Name { get; private set; }
        public int Percentage { get; private set; }
        public string Message { get; private set; }

        public BackgroundWorkProgressEventArgs(long workId, string name, int percentage, string message)
        {
            WorkId = workId;
            Name = name ?? "";
            Percentage = percentage < -1 ? -1 : Math.Min(100, percentage);
            Message = message ?? "";
        }
    }

    internal sealed class BackgroundWorkCompletedEventArgs : EventArgs
    {
        public long WorkId { get; private set; }
        public string Name { get; private set; }
        public bool Cancelled { get; private set; }
        public Exception Error { get; private set; }

        public bool Succeeded { get { return !Cancelled && Error == null; } }

        public BackgroundWorkCompletedEventArgs(long workId, string name, bool cancelled, Exception error)
        {
            WorkId = workId;
            Name = name ?? "";
            Cancelled = cancelled;
            Error = error;
        }
    }

    /// <summary>
    /// Context passed to work running on the queue thread. Report callbacks and
    /// completion events are raised on that same background thread.
    /// </summary>
    internal sealed class BackgroundWorkContext
    {
        private readonly Action<int, string> _report;

        internal BackgroundWorkContext(long workId, string name, CancellationToken token, Action<int, string> report)
        {
            WorkId = workId;
            Name = name ?? "";
            CancellationToken = token;
            _report = report;
        }

        public long WorkId { get; private set; }
        public string Name { get; private set; }
        public CancellationToken CancellationToken { get; private set; }
        public bool IsCancellationRequested { get { return CancellationToken.IsCancellationRequested; } }

        public void ThrowIfCancellationRequested() { CancellationToken.ThrowIfCancellationRequested(); }

        /// <param name="percentage">-1 for indeterminate progress, otherwise 0-100.</param>
        public void ReportProgress(int percentage, string message)
        {
            if (_report != null) _report(percentage, message ?? "");
        }
    }

    /// <summary>
    /// A single background thread that executes queued operations in FIFO order.
    /// It has no dependency on Windows Forms and never marshals callbacks to UI.
    /// Cancellation is cooperative; work should inspect its context regularly.
    /// </summary>
    internal sealed class BackgroundWorkQueue : IDisposable
    {
        private static readonly TimeSpan InfiniteWait = TimeSpan.FromMilliseconds(Timeout.Infinite);

        private sealed class WorkItem
        {
            public long Id;
            public string Name;
            public Action<BackgroundWorkContext> Action;
            public CancellationTokenSource Cancellation;
        }

        private readonly object _sync = new object();
        private readonly Queue<WorkItem> _pending = new Queue<WorkItem>();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly ManualResetEvent _idle = new ManualResetEvent(true);
        private readonly ManualResetEvent _stopped = new ManualResetEvent(false);
        private readonly Thread _thread;
        private WorkItem _current;
        private long _nextId;
        private bool _disposeRequested;
        private bool _suppressEvents;
        private bool _waitHandlesClosed;

        public event EventHandler<BackgroundWorkProgressEventArgs> ProgressChanged;
        public event EventHandler<BackgroundWorkCompletedEventArgs> WorkCompleted;

        public BackgroundWorkQueue()
            : this("RelationshipGraph.BackgroundWork")
        {
        }

        public BackgroundWorkQueue(string threadName)
        {
            _thread = new Thread(Run);
            _thread.IsBackground = true;
            _thread.Name = String.IsNullOrWhiteSpace(threadName) ? "RelationshipGraph.BackgroundWork" : threadName;
            _thread.Start();
        }

        public int PendingCount
        {
            get { lock (_sync) return _pending.Count; }
        }

        public bool IsBusy
        {
            get { lock (_sync) return _current != null || _pending.Count > 0; }
        }

        internal bool IsWorkerThread { get { return Thread.CurrentThread == _thread; } }

        public long Enqueue(string name, Action<BackgroundWorkContext> action)
        {
            if (action == null) throw new ArgumentNullException("action");
            lock (_sync)
            {
                ThrowIfDisposed();
                long id = Interlocked.Increment(ref _nextId);
                WorkItem item = new WorkItem
                {
                    Id = id,
                    Name = name ?? "",
                    Action = action,
                    Cancellation = new CancellationTokenSource()
                };
                _pending.Enqueue(item);
                _idle.Reset();
                _wake.Set();
                return id;
            }
        }

        public bool Cancel(long workId)
        {
            CancellationTokenSource cancellation = null;
            lock (_sync)
            {
                if (_disposeRequested) return false;
                if (_current != null && _current.Id == workId) cancellation = _current.Cancellation;
                if (cancellation == null)
                {
                    foreach (WorkItem item in _pending)
                    {
                        if (item.Id == workId) { cancellation = item.Cancellation; break; }
                    }
                }
            }
            if (cancellation == null) return false;
            TryCancel(cancellation);
            TrySignal(_wake);
            return true;
        }

        public void CancelAll()
        {
            List<CancellationTokenSource> cancellations = new List<CancellationTokenSource>();
            lock (_sync)
            {
                if (_disposeRequested) return;
                if (_current != null) cancellations.Add(_current.Cancellation);
                foreach (WorkItem item in _pending) cancellations.Add(item.Cancellation);
            }
            foreach (CancellationTokenSource cancellation in cancellations) TryCancel(cancellation);
            TrySignal(_wake);
        }

        /// <summary>
        /// Waits until no operation is running or queued. Do not call this from
        /// work executed by this queue.
        /// </summary>
        public bool WaitForIdle(TimeSpan timeout)
        {
            if (timeout != InfiniteWait && (timeout < TimeSpan.Zero || timeout.TotalMilliseconds > Int32.MaxValue)) throw new ArgumentOutOfRangeException("timeout");
            if (IsWorkerThread) return false;
            lock (_sync)
            {
                if (_waitHandlesClosed) return true;
            }
            try { return _idle.WaitOne(timeout); }
            catch (ObjectDisposedException) { return true; }
        }

        private void Run()
        {
            try
            {
                while (true)
                {
                    _wake.WaitOne();
                    while (true)
                    {
                        WorkItem item;
                        lock (_sync)
                        {
                            if (_pending.Count == 0)
                            {
                                if (_current == null) _idle.Set();
                                if (_disposeRequested) return;
                                break;
                            }
                            item = _pending.Dequeue();
                            _current = item;
                        }

                        BackgroundWorkCompletedEventArgs completed = Execute(item);

                        lock (_sync)
                        {
                            if (_current == item) _current = null;
                        }
                        RaiseCompleted(completed);
                        lock (_sync) { if (_current == null && _pending.Count == 0) _idle.Set(); }
                    }
                }
            }
            finally
            {
                _stopped.Set();
            }
        }

        private BackgroundWorkCompletedEventArgs Execute(WorkItem item)
        {
            Exception error = null;
            bool cancelled = item.Cancellation.IsCancellationRequested;
            try
            {
                if (!cancelled)
                {
                    BackgroundWorkContext context = new BackgroundWorkContext(
                        item.Id,
                        item.Name,
                        item.Cancellation.Token,
                        delegate(int percentage, string message) { RaiseProgress(item, percentage, message); });
                    item.Action(context);
                    cancelled = item.Cancellation.IsCancellationRequested;
                }
            }
            catch (OperationCanceledException)
            {
                if (item.Cancellation.IsCancellationRequested) cancelled = true;
                else error = new OperationCanceledException("后台操作在未请求取消时终止。");
            }
            catch (Exception workError)
            {
                error = workError;
            }
            finally
            {
                item.Cancellation.Dispose();
            }
            return new BackgroundWorkCompletedEventArgs(item.Id, item.Name, cancelled, error);
        }

        private void RaiseProgress(WorkItem item, int percentage, string message)
        {
            if (item.Cancellation.IsCancellationRequested) return;
            EventHandler<BackgroundWorkProgressEventArgs> handlers;
            lock (_sync) { if (_suppressEvents) return; handlers = ProgressChanged; }
            if (handlers == null) return;
            BackgroundWorkProgressEventArgs args = new BackgroundWorkProgressEventArgs(item.Id, item.Name, percentage, message);
            foreach (Delegate value in handlers.GetInvocationList())
            {
                try { ((EventHandler<BackgroundWorkProgressEventArgs>)value)(this, args); }
                catch { }
            }
        }

        private void RaiseCompleted(BackgroundWorkCompletedEventArgs args)
        {
            EventHandler<BackgroundWorkCompletedEventArgs> handlers;
            lock (_sync) { if (_suppressEvents) return; handlers = WorkCompleted; }
            if (handlers == null) return;
            foreach (Delegate value in handlers.GetInvocationList())
            {
                try { ((EventHandler<BackgroundWorkCompletedEventArgs>)value)(this, args); }
                catch { }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposeRequested) throw new ObjectDisposedException("BackgroundWorkQueue");
        }

        private static void TryCancel(CancellationTokenSource cancellation)
        {
            if (cancellation == null) return;
            try { cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
            catch (AggregateException) { }
        }

        private static void TrySignal(EventWaitHandle waitHandle)
        {
            if (waitHandle == null) return;
            try { waitHandle.Set(); }
            catch (ObjectDisposedException) { }
        }

        public void Dispose()
        {
            List<CancellationTokenSource> cancellations = new List<CancellationTokenSource>();
            lock (_sync)
            {
                if (_disposeRequested) return;
                _disposeRequested = true;
                _suppressEvents = true;
                if (_current != null) cancellations.Add(_current.Cancellation);
                foreach (WorkItem item in _pending) cancellations.Add(item.Cancellation);
            }

            foreach (CancellationTokenSource cancellation in cancellations) TryCancel(cancellation);
            _wake.Set();

            // The queue thread cannot wait for or close resources it still uses.
            // It will leave them for normal WaitHandle finalization after exiting.
            if (IsWorkerThread) return;

            bool stopped;
            try { stopped = _stopped.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (ObjectDisposedException) { stopped = true; }
            if (!stopped) return;

            lock (_sync)
            {
                if (_waitHandlesClosed) return;
                _waitHandlesClosed = true;
            }
            _wake.Close();
            _idle.Close();
            _stopped.Close();
        }
    }

    /// <summary>
    /// Keeps only the latest pending snapshot during a debounce window and feeds
    /// snapshots to one serial consumer. The caller owns snapshot immutability.
    /// </summary>
    internal sealed class DebouncedSaveQueue<T> : IDisposable
    {
        private static readonly TimeSpan InfiniteWait = TimeSpan.FromMilliseconds(Timeout.Infinite);

        private readonly object _sync = new object();
        private readonly BackgroundWorkQueue _worker;
        private readonly Action<T, BackgroundWorkContext> _saveAction;
        private readonly int _debounceMilliseconds;
        private Timer _timer;
        private T _latestSnapshot;
        private bool _hasPendingSnapshot;
        private bool _debounceReady;
        private DateTime _debounceDueUtc;
        private bool _disposed;

        public event EventHandler<BackgroundWorkProgressEventArgs> ProgressChanged;
        public event EventHandler<BackgroundWorkCompletedEventArgs> SaveCompleted;

        public DebouncedSaveQueue(TimeSpan debounce, Action<T, BackgroundWorkContext> saveAction)
        {
            if (saveAction == null) throw new ArgumentNullException("saveAction");
            if (debounce < TimeSpan.Zero || debounce.TotalMilliseconds > Int32.MaxValue) throw new ArgumentOutOfRangeException("debounce");
            _debounceMilliseconds = (int)Math.Ceiling(debounce.TotalMilliseconds);
            _saveAction = saveAction;
            _worker = new BackgroundWorkQueue("RelationshipGraph.Autosave");
            _worker.ProgressChanged += WorkerProgressChanged;
            _worker.WorkCompleted += WorkerCompleted;
            _timer = new Timer(DebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
        }

        public bool IsBusy
        {
            get { lock (_sync) return _hasPendingSnapshot || _worker.IsBusy; }
        }

        /// <summary>
        /// Replaces the not-yet-submitted snapshot and restarts the debounce time.
        /// </summary>
        public void QueueSave(T snapshot)
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                _latestSnapshot = snapshot;
                _hasPendingSnapshot = true;
                _debounceReady = false;
                _debounceDueUtc = DateTime.UtcNow.AddMilliseconds(_debounceMilliseconds);
                _timer.Change(_debounceMilliseconds, Timeout.Infinite);
            }
        }

        /// <summary>
        /// Immediately submits pending data and waits for all serial saves. If
        /// producers keep posting concurrently, the call waits until timeout.
        /// </summary>
        public bool Flush(TimeSpan timeout)
        {
            if (timeout != InfiniteWait && (timeout < TimeSpan.Zero || timeout.TotalMilliseconds > Int32.MaxValue)) throw new ArgumentOutOfRangeException("timeout");
            if (_worker.IsWorkerThread) return false;
            Stopwatch elapsed = Stopwatch.StartNew();
            while (true)
            {
                lock (_sync)
                {
                    if (_disposed) return false;
                    if (_hasPendingSnapshot)
                    {
                        _timer.Change(Timeout.Infinite, Timeout.Infinite);
                        _debounceReady = true;
                        if (_worker.PendingCount == 0) SubmitPendingLocked();
                    }
                }

                TimeSpan remaining = timeout == InfiniteWait ? InfiniteWait : timeout - elapsed.Elapsed;
                if (remaining < TimeSpan.Zero) return false;
                if (!_worker.WaitForIdle(remaining)) return false;

                lock (_sync)
                {
                    if (!_hasPendingSnapshot) return true;
                }
            }
        }

        public void CancelPending()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _hasPendingSnapshot = false;
                _debounceReady = false;
                _debounceDueUtc = DateTime.MinValue;
                _latestSnapshot = default(T);
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        public void CancelAll()
        {
            CancelPending();
            _worker.CancelAll();
        }

        private void DebounceElapsed(object state)
        {
            lock (_sync)
            {
                if (_disposed || !_hasPendingSnapshot) return;
                TimeSpan remaining = _debounceDueUtc - DateTime.UtcNow;
                if (remaining > TimeSpan.Zero)
                {
                    int due = (int)Math.Min(Int32.MaxValue, Math.Max(1, Math.Ceiling(remaining.TotalMilliseconds)));
                    _timer.Change(due, Timeout.Infinite);
                    return;
                }
                _debounceReady = true;
                if (!_worker.IsBusy) SubmitPendingLocked();
            }
        }

        private void SubmitPendingLocked()
        {
            T snapshot = _latestSnapshot;
            _latestSnapshot = default(T);
            _hasPendingSnapshot = false;
            _debounceReady = false;
            _debounceDueUtc = DateTime.MinValue;
            _worker.Enqueue("autosave", delegate(BackgroundWorkContext context) { _saveAction(snapshot, context); });
        }

        private void WorkerProgressChanged(object sender, BackgroundWorkProgressEventArgs e)
        {
            EventHandler<BackgroundWorkProgressEventArgs> handlers = ProgressChanged;
            if (handlers == null) return;
            foreach (Delegate value in handlers.GetInvocationList())
            {
                try { ((EventHandler<BackgroundWorkProgressEventArgs>)value)(this, e); }
                catch { }
            }
        }

        private void WorkerCompleted(object sender, BackgroundWorkCompletedEventArgs e)
        {
            lock (_sync)
            {
                if (!_disposed && _hasPendingSnapshot && _debounceReady && !_worker.IsBusy) SubmitPendingLocked();
            }
            EventHandler<BackgroundWorkCompletedEventArgs> handlers = SaveCompleted;
            if (handlers == null) return;
            foreach (Delegate value in handlers.GetInvocationList())
            {
                try { ((EventHandler<BackgroundWorkCompletedEventArgs>)value)(this, e); }
                catch { }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException("DebouncedSaveQueue");
        }

        public void Dispose()
        {
            Timer timer;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _hasPendingSnapshot = false;
                _debounceReady = false;
                _debounceDueUtc = DateTime.MinValue;
                _latestSnapshot = default(T);
                timer = _timer;
                _timer = null;
            }

            if (timer != null) timer.Dispose();
            _worker.ProgressChanged -= WorkerProgressChanged;
            _worker.WorkCompleted -= WorkerCompleted;
            _worker.Dispose();
        }
    }
}
