// Import this file via NinjaTrader Tools -> Import -> NinjaScript Add-On.
// It opens the bridge WPF window from NT menu New -> Transaq Bridge.
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns;

namespace NinjaTrader.NinjaScript.AddOns
{
    public class TransaqBridgeAddOn : AddOnBase
    {
        protected override void OnWindowCreated(System.Windows.Window window)
        {
            if (window is ControlCenter)
            {
                var item = new System.Windows.Controls.MenuItem { Header = "Transaq Bridge" };
                item.Click += delegate
                {
                    var bridgeWindow = new NinjaTrader8.AddOn.TransaqBridge.TransaqBridgeWindow("transaq-nt8");
                    bridgeWindow.Show();
                };

                ((ControlCenter)window).MainMenu.Items.Add(item);
            }
        }
    }
}
