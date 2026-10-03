using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DownloadsOSX.Services
{
    /// <summary>
    /// A single background STA thread for shell COM work (thumbnails, GIF frame compositing).
    /// High-priority jobs (preview) jump ahead of the list thumbnails.
    /// </summary>
    internal sealed class StaWorker : IDisposable
    {
        private readonly LinkedList<Action> _queue = new LinkedList<Action>();
        private readonly object _gate = new object();
        private readonly Thread _thread;
        private bool _disposed;

        public StaWorker(string name)
        {
            _thread = new Thread(Loop) { IsBackground = true, Name = name, Priority = ThreadPriority.BelowNormal };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        public Task<T> Run<T>(Func<T> work, bool highPriority = false)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action job = () =>
            {
                try { tcs.TrySetResult(work()); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            };
            lock (_gate)
            {
                if (_disposed) { tcs.TrySetCanceled(); return tcs.Task; }
                if (highPriority) _queue.AddFirst(job); else _queue.AddLast(job);
                Monitor.Pulse(_gate);
            }
            return tcs.Task;
        }

        private void Loop()
        {
            while (true)
            {
                Action job;
                lock (_gate)
                {
                    while (_queue.Count == 0 && !_disposed) Monitor.Wait(_gate);
                    if (_disposed) return;
                    job = _queue.First.Value;
                    _queue.RemoveFirst();
                }
                job();
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _queue.Clear();
                Monitor.PulseAll(_gate);
            }
        }
    }
}
