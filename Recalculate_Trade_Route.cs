using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using zip.lexy.tgame.state.ship;
using Smart_Trade.Get_Class;
using Smart_Trade.Process_Route_Sheets;

namespace Smart_Trade.Recalculate_Trade_Route
{
    internal class Recalculate_Trade_Route
    {
        // This is the main method that orchestrates the entire recalculation process for a ship's trade route.
        // It gathers all necessary data and then delegates to a helper method that processes each trade sheet in the route.
        public static void RecalculateTradeRoute(Ship _ship)
        {
            var _gameState = Get_Class.Get_Class.GetGameState();
            var _priceCalc = Get_Class.Get_Class.GetPriceCalculator();

            if (_gameState == null || _priceCalc == null || Get_Class.Get_Class.GetTradeRoute(_ship) == null) return;

            var _convoy = Get_Class.Get_Class.GetConvoy(_ship);
            var _tradeRoute = Get_Class.Get_Class.GetTradeRoute(_ship);
            var _routeCities = Get_Class.Get_Class.GetCurrentSheet(_tradeRoute, _gameState);

            List<(string, int, float, int, int)> _candidates = Get_Class.Get_Class.GetCandidate();
            var _cargoSource = Get_Class.Get_Class.GetCargoHolder(_convoy);

            // Now the main method is just a few clear steps
            Process_Route_Sheets.Process_Route_Sheets.ProcessRouteSheets(_tradeRoute, _routeCities, _gameState, _priceCalc, _cargoSource, _candidates);
        }

    }
}
