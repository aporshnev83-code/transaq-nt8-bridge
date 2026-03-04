using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Transaq.Bridge.Core
{
    public class BlockingTextQueue
    {
        private readonly BlockingCollection<string> _queue = new BlockingCollection<string>(new ConcurrentQueue<string>());

        public void Enqueue(string text)
        {
            if (!_queue.IsAddingCompleted)
            {
                _queue.Add(text);
            }
        }

        public bool TryDequeue(out string text, CancellationToken token)
        {
            try
            {
                text = _queue.Take(token);
                return true;
            }
            catch (OperationCanceledException)
            {
                text = null;
                return false;
            }
            catch (InvalidOperationException)
            {
                text = null;
                return false;
            }
        }

        public void Complete()
        {
            _queue.CompleteAdding();
        }
    }
}
