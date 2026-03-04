using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Transaq.Bridge.Core;

namespace NinjaTrader8.AddOn.TransaqBridge
{
    public class PipeBridgeClient
    {
        private readonly string _pipeName;
        private readonly Action<string> _log;
        private NamedPipeClientStream _pipe;
        private StreamWriter _writer;
        private CancellationTokenSource _cts;

        public event Action<bool> ConnectedChanged;

        public PipeBridgeClient(string pipeName, Action<string> log)
        {
            _pipeName = pipeName;
            _log = log;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            Task.Run(() => ReconnectLoop(_cts.Token), _cts.Token);
        }

        public void Stop()
        {
            if (_cts != null)
            {
                _cts.Cancel();
            }
            Close();
        }

        public void Send(string type, object payload)
        {
            var writer = _writer;
            if (writer == null)
            {
                return;
            }

            lock (this)
            {
                writer.Write(JsonLineCodec.Serialize(Envelope.Create(type, payload)));
                writer.Flush();
            }
        }

        private async Task ReconnectLoop(CancellationToken token)
        {
            var delayMs = 500;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    _pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    await _pipe.ConnectAsync(3000, token).ConfigureAwait(false);
                    _writer = new StreamWriter(_pipe, JsonLineCodec.Utf8NoBom) { AutoFlush = true };
                    RaiseConnected(true);
                    _log("Connected to gateway");
                    delayMs = 500;
                    await ReadLoop(token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log("Pipe reconnect: " + ex.Message);
                }
                finally
                {
                    Close();
                    RaiseConnected(false);
                }

                await Task.Delay(delayMs, token).ConfigureAwait(false);
                delayMs = Math.Min(delayMs * 2, 10000);
            }
        }

        private async Task ReadLoop(CancellationToken token)
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

                    var env = JsonLineCodec.Deserialize(line);
                    if (env.Type == "ping")
                    {
                        Send("pong", new { });
                    }
                    else if (env.Type == "marketData")
                    {
                        BridgeEvents.RaiseMarket(env.Payload.ToObject<MarketDataSnapshot>());
                    }
                    else if (env.Type == "dom")
                    {
                        BridgeEvents.RaiseDom(env.Payload.ToObject<DomSnapshot>());
                    }
                    else if (env.Type == "orderUpdate")
                    {
                        BridgeEvents.RaiseOrder(env.Payload.ToObject<OrderUpdate>());
                    }
                }
            }
        }

        private void Close()
        {
            try
            {
                if (_writer != null)
                {
                    _writer.Dispose();
                }
            }
            catch { }

            try
            {
                if (_pipe != null)
                {
                    _pipe.Dispose();
                }
            }
            catch { }

            _writer = null;
            _pipe = null;
        }

        private void RaiseConnected(bool connected)
        {
            var handler = ConnectedChanged;
            if (handler != null)
            {
                handler(connected);
            }
        }
    }
}
