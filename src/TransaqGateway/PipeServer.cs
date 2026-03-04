using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Transaq.Bridge.Core;

namespace TransaqGateway
{
    public class PipeServer
    {
        private readonly string _pipeName;
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<int, PipeClientConnection> _clients;
        private int _nextId;

        public event Action<Envelope> MessageReceived;

        public PipeServer(string pipeName, Logger logger)
        {
            _pipeName = pipeName;
            _logger = logger;
            _clients = new ConcurrentDictionary<int, PipeClientConnection>();
        }

        public void Start(CancellationToken token)
        {
            Task.Run(() => AcceptLoop(token), token);
            Task.Run(() => HeartbeatLoop(token), token);
        }

        public void Broadcast(string type, object payload)
        {
            var line = JsonLineCodec.Serialize(Envelope.Create(type, payload));
            foreach (var c in _clients.Values)
            {
                c.TrySend(line);
            }
        }

        private async Task AcceptLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                NamedPipeServerStream server = null;
                try
                {
                    server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 10, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (server != null)
                    {
                        server.Dispose();
                    }
                    break;
                }
                var id = Interlocked.Increment(ref _nextId);
                var conn = new PipeClientConnection(id, server, RemoveClient, _logger, RaiseMessage);
                _clients[id] = conn;
                conn.Start(token);
                _logger.Info("Pipe client connected: " + id);
                conn.TrySend(JsonLineCodec.Serialize(Envelope.Create("hello", new { server = "TransaqGateway" })));
            }
        }

        private void RaiseMessage(Envelope envelope)
        {
            var handler = MessageReceived;
            if (handler != null)
            {
                handler(envelope);
            }
        }

        private void RemoveClient(int id)
        {
            PipeClientConnection removed;
            _clients.TryRemove(id, out removed);
            _logger.Info("Pipe client disconnected: " + id);
        }

        private async Task HeartbeatLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                Broadcast("ping", new JObject());
                await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
            }
        }

        private class PipeClientConnection
        {
            private readonly int _id;
            private readonly NamedPipeServerStream _pipe;
            private readonly Action<int> _onClose;
            private readonly Logger _logger;
            private readonly Action<Envelope> _onMessage;
            private readonly BlockingCollection<string> _sendQueue;

            public PipeClientConnection(int id, NamedPipeServerStream pipe, Action<int> onClose, Logger logger, Action<Envelope> onMessage)
            {
                _id = id;
                _pipe = pipe;
                _onClose = onClose;
                _logger = logger;
                _onMessage = onMessage;
                _sendQueue = new BlockingCollection<string>();
            }

            public void Start(CancellationToken token)
            {
                Task.Run(() => ReadLoop(token), token);
                Task.Run(() => WriteLoop(token), token);
            }

            public void TrySend(string line)
            {
                if (!_sendQueue.IsAddingCompleted)
                {
                    try
                    {
                        _sendQueue.Add(line);
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }

            private async Task ReadLoop(CancellationToken token)
            {
                try
                {
                    using (var reader = new StreamReader(_pipe, JsonLineCodec.Utf8NoBom))
                    {
                        while (!token.IsCancellationRequested && _pipe.IsConnected)
                        {
                            var line = await reader.ReadLineAsync().ConfigureAwait(false);
                            if (line == null)
                            {
                                break;
                            }

                            Envelope env;
                            try
                            {
                                env = JsonLineCodec.Deserialize(line);
                            }
                            catch (Exception ex)
                            {
                                _logger.Error("Pipe parse error: " + ex.Message);
                                continue;
                            }

                            _onMessage(env);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("Pipe read error: " + ex.Message);
                }
                finally
                {
                    _sendQueue.CompleteAdding();
                    _pipe.Dispose();
                    _onClose(_id);
                }
            }

            private async Task WriteLoop(CancellationToken token)
            {
                try
                {
                    using (var writer = new StreamWriter(_pipe, JsonLineCodec.Utf8NoBom) { AutoFlush = true })
                    {
                        while (!token.IsCancellationRequested && _pipe.IsConnected)
                        {
                            string line;
                            try
                            {
                                line = _sendQueue.Take(token);
                            }
                            catch
                            {
                                break;
                            }

                            await writer.WriteAsync(line).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("Pipe write error: " + ex.Message);
                }
            }
        }
    }
}
