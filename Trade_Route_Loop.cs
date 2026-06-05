using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using zip.lexy.tgame.state.trader;
using Smart_Trade.Recalculate_Trade_Route;

namespace Smart_Trade.Trade_Route_Loop
{
    internal class Trade_Route_Loop
    {
        // This method iterates through all the player's ships and recalculates their trade routes if they have an active one.
        // It's called on season changes to adapt to new market conditions.
        public static void TradeRouteLoop(Human _playerTrader)
        {
            foreach (var ship in _playerTrader.ships)
            {
                if (ship?.convoy?.tradeRoute != null && ship.convoy.tradeRoute.active)
                {
                    Recalculate_Trade_Route.Recalculate_Trade_Route.RecalculateTradeRoute(ship);
                }
            }
        }
    }
}
