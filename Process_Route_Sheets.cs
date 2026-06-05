using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using zip.lexy.tgame.state;
using zip.lexy.tgame.state.city;
using zip.lexy.tgame.state.ship;
using zip.lexy.tgame.state.ship.trading;
using zip.lexy.tgame.ui.gamegeneration;
using Smart_Trade.Sell_Logic;
using Smart_Trade.Buy_Logic;
using Smart_Trade.Add_Actions;
using Smart_Trade.Disable_Actions;
using Smart_Trade.Apply_Trade_Actions;

namespace Smart_Trade.Process_Route_Sheets
{
    internal class Process_Route_Sheets
    {
        public static void ProcessRouteSheets(
            TradeRoute _tradeRoute,
            List<City> _routeCities,
            GameState _gameState,
            PriceCalculator _priceCalc,
            CargoHolder _cargoSource,
            List<(string, int, float, int, int)> _candidates)
        {
            for (int i = 0; i < _tradeRoute.tradeSheets.Count; i++)
            {
                var currentSheet = _tradeRoute.tradeSheets[i];
                var currentCity = _gameState.cities[currentSheet.cityId];
                var futureCities = _routeCities.Skip(i + 1).ToList();

                // Clean up the _candidates list for each city so old scores don't carry over
                _candidates.Clear();

                // --- STEP 1: PREPARE SHEET ---
                Smart_Trade.Add_Actions.Add_Actions.AddActions(currentSheet);
                Smart_Trade.Disable_Actions.DisableActionsClass.DisableActions(currentSheet);

                // --- STEP 2: GENERATE POSSIBILITIES ---
                Smart_Trade.Sell_Logic.Sell_Logic.SellLogic(_cargoSource, _gameState, currentCity, futureCities, _priceCalc, _candidates);
                Smart_Trade.Buy_Logic.Buy_Logic.BuyLogic(currentCity, _gameState, futureCities, _priceCalc, _candidates);

                // --- STEP 3: EXECUTE BEST CHOICE ---
                Smart_Trade.Apply_Trade_Actions.Apply_Trade_Actions.ApplyTradeActions(_candidates, currentSheet, currentCity, _tradeRoute);
            }
        }
    }
}
