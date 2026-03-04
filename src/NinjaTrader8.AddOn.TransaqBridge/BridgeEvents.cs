using System;
using System.Collections.Generic;
using Transaq.Bridge.Core;

namespace NinjaTrader8.AddOn.TransaqBridge
{
    public static class BridgeEvents
    {
        public static event Action<InstrumentKey, decimal?, decimal?, decimal?> OnMarketData;
        public static event Action<InstrumentKey, IList<DomLevel>> OnDom;
        public static event Action<OrderUpdate> OnOrderUpdate;

        public static void RaiseMarket(MarketDataSnapshot md)
        {
            var handler = OnMarketData;
            if (handler != null)
            {
                handler(md.Instrument, md.Bid, md.Ask, md.Last);
            }
        }

        public static void RaiseDom(DomSnapshot dom)
        {
            var handler = OnDom;
            if (handler != null)
            {
                handler(dom.Instrument, dom.Levels);
            }
        }

        public static void RaiseOrder(OrderUpdate update)
        {
            var handler = OnOrderUpdate;
            if (handler != null)
            {
                handler(update);
            }
        }
    }
}
