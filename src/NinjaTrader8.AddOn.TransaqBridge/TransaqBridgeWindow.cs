using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Transaq.Bridge.Core;

namespace NinjaTrader8.AddOn.TransaqBridge
{
    public class TransaqBridgeWindow : Window
    {
        private PipeBridgeClient _client;
        private readonly TextBox _pipeName;
        private readonly TextBox _gatewayPath;
        private readonly TextBox _board;
        private readonly TextBox _seccode;
        private readonly TextBox _price;
        private readonly TextBlock _state;
        private readonly TextBlock _bid;
        private readonly TextBlock _ask;
        private readonly TextBlock _last;
        private readonly ObservableCollection<DomLevel> _dom;
        private readonly ObservableCollection<OrderUpdate> _orders;
        private string _lastClientOrderId;

        public TransaqBridgeWindow(string defaultPipeName)
        {
            Title = "Transaq Bridge";
            Width = 980;
            Height = 620;

            _dom = new ObservableCollection<DomLevel>();
            _orders = new ObservableCollection<OrderUpdate>();

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());

            var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
            _pipeName = AddLabeledText(top, "Pipe", defaultPipeName, 120);
            _gatewayPath = AddLabeledText(top, "Gateway EXE", "", 260);
            var launch = new Button { Content = "Launch Gateway", Margin = new Thickness(4) };
            launch.Click += delegate { LaunchGateway(); };
            top.Children.Add(launch);

            var connect = new Button { Content = "Connect", Margin = new Thickness(4) };
            connect.Click += delegate { ConnectPipe(); };
            top.Children.Add(connect);

            var disconnect = new Button { Content = "Disconnect", Margin = new Thickness(4) };
            disconnect.Click += delegate { if (_client != null) _client.Stop(); };
            top.Children.Add(disconnect);

            _state = new TextBlock { Text = "Disconnected", Margin = new Thickness(8, 8, 0, 0) };
            top.Children.Add(_state);
            root.Children.Add(top);

            var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
            _board = AddLabeledText(controls, "Board", "TQBR", 60);
            _seccode = AddLabeledText(controls, "Seccode", "SBER", 100);
            var subscribe = new Button { Content = "Subscribe", Margin = new Thickness(4) };
            subscribe.Click += delegate { if (_client != null) _client.Send("subscribe", new { board = _board.Text, seccode = _seccode.Text }); };
            controls.Children.Add(subscribe);
            _price = AddLabeledText(controls, "Price", "0", 80);
            AddOrderButtons(controls);
            Grid.SetRow(controls, 1);
            root.Children.Add(controls);

            var tables = new Grid();
            tables.ColumnDefinitions.Add(new ColumnDefinition());
            tables.ColumnDefinitions.Add(new ColumnDefinition());
            tables.Children.Add(new DataGrid { ItemsSource = _dom, AutoGenerateColumns = true, Margin = new Thickness(8) });
            var ordersGrid = new DataGrid { ItemsSource = _orders, AutoGenerateColumns = true, Margin = new Thickness(8) };
            Grid.SetColumn(ordersGrid, 1);
            tables.Children.Add(ordersGrid);
            Grid.SetRow(tables, 2);
            root.Children.Add(tables);

            var md = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 8), VerticalAlignment = VerticalAlignment.Bottom };
            _bid = new TextBlock { Margin = new Thickness(5), Text = "Bid: -" };
            _ask = new TextBlock { Margin = new Thickness(5), Text = "Ask: -" };
            _last = new TextBlock { Margin = new Thickness(5), Text = "Last: -" };
            md.Children.Add(_bid);
            md.Children.Add(_ask);
            md.Children.Add(_last);
            Grid.SetRow(md, 2);
            root.Children.Add(md);

            Content = root;
            Closed += delegate { if (_client != null) _client.Stop(); };

            BridgeEvents.OnMarketData += OnMarket;
            BridgeEvents.OnDom += OnDom;
            BridgeEvents.OnOrderUpdate += OnOrder;
        }

        private void ConnectPipe()
        {
            if (_client != null)
            {
                _client.Stop();
            }

            _client = new PipeBridgeClient(_pipeName.Text, Log);
            _client.ConnectedChanged += OnConnected;
            _client.Start();
        }

        private void LaunchGateway()
        {
            if (string.IsNullOrWhiteSpace(_gatewayPath.Text))
            {
                Log("Gateway path is empty");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _gatewayPath.Text,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(_gatewayPath.Text),
                    UseShellExecute = true
                });
                Log("Gateway launched");
            }
            catch (Exception ex)
            {
                Log("Gateway launch failed: " + ex.Message);
            }
        }

        private TextBox AddLabeledText(Panel panel, string label, string value, int width)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(4, 8, 2, 0) });
            var tb = new TextBox { Text = value, Width = width, Margin = new Thickness(2, 4, 8, 4) };
            panel.Children.Add(tb);
            return tb;
        }

        private void AddOrderButtons(StackPanel controls)
        {
            var mBuy = new Button { Content = "Market Buy", Margin = new Thickness(4) };
            mBuy.Click += delegate { SendOrder("Buy", "Market", null); };
            controls.Children.Add(mBuy);
            var mSell = new Button { Content = "Market Sell", Margin = new Thickness(4) };
            mSell.Click += delegate { SendOrder("Sell", "Market", null); };
            controls.Children.Add(mSell);
            var lBuy = new Button { Content = "Limit Buy", Margin = new Thickness(4) };
            lBuy.Click += delegate { SendOrder("Buy", "Limit", ParsePrice()); };
            controls.Children.Add(lBuy);
            var lSell = new Button { Content = "Limit Sell", Margin = new Thickness(4) };
            lSell.Click += delegate { SendOrder("Sell", "Limit", ParsePrice()); };
            controls.Children.Add(lSell);
            var cancel = new Button { Content = "Cancel Last", Margin = new Thickness(4) };
            cancel.Click += delegate { if (_client != null) _client.Send("cancelOrder", new CancelOrderCommand { ClientOrderId = _lastClientOrderId }); };
            controls.Children.Add(cancel);
        }

        private decimal? ParsePrice()
        {
            decimal d;
            return decimal.TryParse(_price.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out d) ? d : (decimal?)null;
        }

        private void SendOrder(string side, string type, decimal? price)
        {
            if (_client == null)
            {
                return;
            }
            _lastClientOrderId = Guid.NewGuid().ToString("N");
            _client.Send("newOrder", new NewOrderCommand
            {
                ClientOrderId = _lastClientOrderId,
                Instrument = new InstrumentKey { Board = _board.Text, SecCode = _seccode.Text },
                Side = side,
                OrderType = type,
                Quantity = 1,
                Price = price
            });
        }

        private void OnConnected(bool connected)
        {
            Dispatcher.Invoke(delegate { _state.Text = connected ? "Connected" : "Disconnected"; });
        }

        private void OnMarket(InstrumentKey key, decimal? bid, decimal? ask, decimal? last)
        {
            Dispatcher.Invoke(delegate
            {
                _bid.Text = "Bid: " + (bid.HasValue ? bid.Value.ToString(CultureInfo.InvariantCulture) : "-");
                _ask.Text = "Ask: " + (ask.HasValue ? ask.Value.ToString(CultureInfo.InvariantCulture) : "-");
                _last.Text = "Last: " + (last.HasValue ? last.Value.ToString(CultureInfo.InvariantCulture) : "-");
            });
        }

        private void OnDom(InstrumentKey key, System.Collections.Generic.IList<DomLevel> levels)
        {
            Dispatcher.Invoke(delegate
            {
                _dom.Clear();
                foreach (var l in levels)
                {
                    _dom.Add(l);
                }
            });
        }

        private void OnOrder(OrderUpdate update)
        {
            Dispatcher.Invoke(() => _orders.Add(update));
        }

        private void Log(string msg)
        {
            Debug.WriteLine("[TransaqBridge] " + msg);
        }
    }
}
