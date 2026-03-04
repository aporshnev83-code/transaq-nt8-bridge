using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Transaq.Bridge.Core;

namespace TransaqGateway
{
    public class GatewayService
    {
        private readonly AppConfig _config;
        private readonly Logger _logger;
        private readonly TransaqApi _api;
        private readonly PipeServer _pipes;
        private readonly BlockingTextQueue _callbackQueue;
        private readonly Dictionary<string, MarketDataSnapshot> _quotes;
        private readonly Dictionary<string, SortedDictionary<decimal, DomLevel>> _books;
        private readonly Dictionary<string, long> _clientOrderToTx;
        private CancellationTokenSource _cts;

        public GatewayService(AppConfig config, Logger logger)
        {
            _config = config;
            _logger = logger;
            _api = new TransaqApi();
            _pipes = new PipeServer(string.IsNullOrWhiteSpace(config.PipeName) ? "transaq-nt8" : config.PipeName, logger);
            _callbackQueue = new BlockingTextQueue();
            _quotes = new Dictionary<string, MarketDataSnapshot>();
            _books = new Dictionary<string, SortedDictionary<decimal, DomLevel>>();
            _clientOrderToTx = new Dictionary<string, long>();
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _pipes.MessageReceived += OnPipeMessage;
            _pipes.Start(_cts.Token);

            _api.Initialize(delegate (string msg)
            {
                _callbackQueue.Enqueue(msg);
                return true;
            });

            Task.Run(() => PumpLoop(_cts.Token), _cts.Token);

            var connectXml = string.Format("<command id=\"connect\"><login>{0}</login><password>{1}</password><host>{2}</host><port>{3}</port><autopos>{4}</autopos></command>",
                _config.Login,
                _config.Password,
                _config.Host,
                _config.Port,
                _config.AutoPos ? "true" : "false");

            var resp = _api.Send(connectXml);
            _logger.Info("Connect command sent for login=" + Mask(_config.Login) + " host=" + _config.Host + ":" + _config.Port + " response=" + Trim(resp));

            if (_config.Instruments != null)
            {
                foreach (var item in _config.Instruments)
                {
                    Subscribe(item.Board, item.Seccode);
                }
            }
        }

        public void Stop()
        {
            if (_cts != null)
            {
                _cts.Cancel();
            }
            _callbackQueue.Complete();
        }

        private void OnPipeMessage(Envelope env)
        {
            if (env == null || string.IsNullOrWhiteSpace(env.Type))
            {
                return;
            }

            if (env.Type == "subscribe")
            {
                Subscribe((string)env.Payload["board"], (string)env.Payload["seccode"]);
                return;
            }

            if (env.Type == "newOrder")
            {
                SendNewOrder(env.Payload.ToObject<NewOrderCommand>());
                return;
            }

            if (env.Type == "cancelOrder")
            {
                SendCancel(env.Payload.ToObject<CancelOrderCommand>());
            }
        }

        private void PumpLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                string raw;
                if (!_callbackQueue.TryDequeue(out raw, token))
                {
                    continue;
                }

                try
                {
                    ParseXml(raw);
                }
                catch (Exception ex)
                {
                    _logger.Error("Pump parse error: " + ex.Message + " xml=" + Trim(raw));
                }
            }
        }

        private void ParseXml(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return;
            }

            var root = XDocument.Parse(xml).Root;
            if (root == null)
            {
                return;
            }

            var name = root.Name.LocalName.ToLowerInvariant();
            if (name == "server_status")
            {
                if ((string)root.Attribute("connected") == "true")
                {
                    _logger.Info("Connected to Transaq server");
                    _pipes.Broadcast("log", new { message = "Connected" });
                    _api.Send("<command id=\"get_forts_positions\" />");
                    _api.Send("<command id=\"get_limits\" />");
                }
                return;
            }

            if (name == "quotation")
            {
                UpdateQuotation(root);
                return;
            }

            if (name == "trade")
            {
                UpdateTrade(root);
                return;
            }

            if (name == "quote")
            {
                UpdateDomLevel(root);
                return;
            }

            if (name == "order")
            {
                PublishOrder(root);
                return;
            }

            if (name == "position")
            {
                PublishPosition(root);
                return;
            }

            if (name == "quotations")
            {
                foreach (var q in root.Elements().Where(x => x.Name.LocalName == "quotation"))
                {
                    UpdateQuotation(q);
                }
                return;
            }

            if (name == "alltrades" || name == "trades" || name == "ticks")
            {
                foreach (var t in root.Elements().Where(x => x.Name.LocalName == "trade"))
                {
                    UpdateTrade(t);
                }
                return;
            }

            if (name == "quotes")
            {
                foreach (var q in root.Elements().Where(x => x.Name.LocalName == "quote"))
                {
                    UpdateDomLevel(q);
                }
                return;
            }

            if (name == "orders")
            {
                foreach (var o in root.Elements().Where(x => x.Name.LocalName == "order"))
                {
                    PublishOrder(o);
                }
                return;
            }

            if (name == "positions")
            {
                foreach (var p in root.Elements().Where(x => x.Name.LocalName == "position"))
                {
                    PublishPosition(p);
                }
            }
        }

        private void UpdateQuotation(XElement x)
        {
            var key = Key((string)x.Attribute("board"), (string)x.Attribute("seccode"));
            var md = EnsureMarketData(key);
            var bid = ParseDecimal((string)x.Attribute("bid"));
            var ask = ParseDecimal((string)x.Attribute("ask"));
            if (bid.HasValue)
            {
                md.Bid = bid;
            }
            if (ask.HasValue)
            {
                md.Ask = ask;
            }
            _pipes.Broadcast("marketData", md);
        }

        private void UpdateTrade(XElement x)
        {
            var key = Key((string)x.Attribute("board"), (string)x.Attribute("seccode"));
            var md = EnsureMarketData(key);
            var price = ParseDecimal((string)x.Attribute("price"));
            if (price.HasValue)
            {
                md.Last = price;
                _pipes.Broadcast("marketData", md);
            }
        }

        private void UpdateDomLevel(XElement x)
        {
            var key = Key((string)x.Attribute("board"), (string)x.Attribute("seccode"));
            SortedDictionary<decimal, DomLevel> levels;
            if (!_books.TryGetValue(key, out levels))
            {
                levels = new SortedDictionary<decimal, DomLevel>();
                _books[key] = levels;
            }

            var price = ParseDecimal((string)x.Attribute("price")) ?? 0m;
            var qty = ParseDecimal((string)x.Attribute("quantity")) ?? 0m;
            var side = ((string)x.Attribute("buysell") ?? string.Empty).ToLowerInvariant();

            DomLevel level;
            if (!levels.TryGetValue(price, out level))
            {
                level = new DomLevel { Price = price };
                levels[price] = level;
            }

            if (side == "buy" || side == "b")
            {
                level.BidSize = qty;
            }
            else
            {
                level.AskSize = qty;
            }

            if (level.BidSize == 0m && level.AskSize == 0m)
            {
                levels.Remove(price);
            }

            var snapshot = new DomSnapshot
            {
                Instrument = ToInstrument(key),
                Levels = levels.Values.OrderByDescending(v => v.Price).Take(10).ToList()
            };
            _pipes.Broadcast("dom", snapshot);
        }

        private void PublishOrder(XElement x)
        {
            var txid = ParseLong((string)x.Attribute("transactionid"));
            var update = new OrderUpdate
            {
                TransactionId = txid,
                OrderNo = (string)x.Attribute("orderno"),
                State = (string)x.Attribute("status") ?? (string)x.Attribute("state"),
                Filled = ParseDecimal((string)x.Attribute("filled")) ?? 0m,
                Message = (string)x.Attribute("result")
            };

            if (txid.HasValue)
            {
                foreach (var pair in _clientOrderToTx)
                {
                    if (pair.Value == txid.Value)
                    {
                        update.ClientOrderId = pair.Key;
                        break;
                    }
                }
            }

            _pipes.Broadcast("orderUpdate", update);
        }

        private void PublishPosition(XElement x)
        {
            var msg = new PositionUpdate
            {
                Instrument = new InstrumentKey { Board = (string)x.Attribute("board"), SecCode = (string)x.Attribute("seccode") },
                Quantity = ParseDecimal((string)x.Attribute("saldo")) ?? 0m,
                AvgPrice = ParseDecimal((string)x.Attribute("price")) ?? 0m
            };
            _pipes.Broadcast("positionUpdate", msg);
        }

        private void Subscribe(string board, string seccode)
        {
            if (string.IsNullOrWhiteSpace(board) || string.IsNullOrWhiteSpace(seccode))
            {
                return;
            }

            var xml = string.Format("<command id=\"subscribe\"><alltrades><security><board>{0}</board><seccode>{1}</seccode></security></alltrades><quotations><security><board>{0}</board><seccode>{1}</seccode></security></quotations><quotes><security><board>{0}</board><seccode>{1}</seccode></security></quotes></command>", board, seccode);
            var resp = _api.Send(xml);
            _logger.Info("Subscribed " + board + "/" + seccode + " response=" + Trim(resp));
        }

        private void SendNewOrder(NewOrderCommand cmd)
        {
            if (cmd == null || cmd.Instrument == null)
            {
                return;
            }

            var bymarket = string.Equals(cmd.OrderType, "Market", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
            var xml = string.Format("<command id=\"neworder\"><board>{0}</board><seccode>{1}</seccode><buysell>{2}</buysell><quantity>{3}</quantity><bymarket>{4}</bymarket>{5}</command>",
                cmd.Instrument.Board,
                cmd.Instrument.SecCode,
                (cmd.Side ?? "Buy").ToUpperInvariant().StartsWith("B") ? "B" : "S",
                cmd.Quantity.ToString(CultureInfo.InvariantCulture),
                bymarket,
                cmd.Price.HasValue ? "<price>" + cmd.Price.Value.ToString(CultureInfo.InvariantCulture) + "</price>" : string.Empty);

            var resp = _api.Send(xml);
            var txText = ExtractTxId(resp);
            long tx;
            long? txValue = null;
            if (long.TryParse(txText, out tx))
            {
                txValue = tx;
                if (!string.IsNullOrWhiteSpace(cmd.ClientOrderId))
                {
                    _clientOrderToTx[cmd.ClientOrderId] = tx;
                }
            }

            _pipes.Broadcast("orderUpdate", new OrderUpdate
            {
                ClientOrderId = cmd.ClientOrderId,
                TransactionId = txValue,
                State = "Sent",
                Message = Trim(resp)
            });
        }

        private void SendCancel(CancelOrderCommand cmd)
        {
            if (cmd == null)
            {
                return;
            }

            long tx = cmd.TransactionId ?? 0;
            if (tx == 0 && !string.IsNullOrWhiteSpace(cmd.ClientOrderId) && _clientOrderToTx.ContainsKey(cmd.ClientOrderId))
            {
                tx = _clientOrderToTx[cmd.ClientOrderId];
            }

            if (tx == 0)
            {
                _pipes.Broadcast("error", new { message = "Cannot cancel: missing transaction id", clientOrderId = cmd.ClientOrderId });
                return;
            }

            var resp = _api.Send(string.Format("<command id=\"cancelorder\"><transactionid>{0}</transactionid></command>", tx));
            _pipes.Broadcast("orderUpdate", new OrderUpdate { ClientOrderId = cmd.ClientOrderId, TransactionId = tx, State = "CancelSent", Message = Trim(resp) });
        }

        private MarketDataSnapshot EnsureMarketData(string key)
        {
            MarketDataSnapshot md;
            if (!_quotes.TryGetValue(key, out md))
            {
                md = new MarketDataSnapshot { Instrument = ToInstrument(key) };
                _quotes[key] = md;
            }
            return md;
        }

        private static string Key(string board, string sec)
        {
            return (board ?? string.Empty) + ":" + (sec ?? string.Empty);
        }

        private static InstrumentKey ToInstrument(string key)
        {
            var parts = key.Split(':');
            return new InstrumentKey
            {
                Board = parts.Length > 0 ? parts[0] : string.Empty,
                SecCode = parts.Length > 1 ? parts[1] : string.Empty
            };
        }

        private static decimal? ParseDecimal(string s)
        {
            decimal d;
            return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out d) ? d : (decimal?)null;
        }

        private static long? ParseLong(string s)
        {
            long n;
            return long.TryParse(s, out n) ? n : (long?)null;
        }

        private static string ExtractTxId(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return string.Empty;
            }

            try
            {
                var root = XDocument.Parse(xml).Root;
                if (root == null)
                {
                    return string.Empty;
                }
                return (string)root.Attribute("transactionid") ?? root.Value;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string Trim(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }
            value = value.Replace("\r", " ").Replace("\n", " ");
            return value.Length > 200 ? value.Substring(0, 200) + "..." : value;
        }

        private static string Mask(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }
            if (value.Length < 3)
            {
                return "***";
            }
            return value.Substring(0, 1) + new string('*', value.Length - 2) + value.Substring(value.Length - 1, 1);
        }
    }
}
