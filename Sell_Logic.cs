using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using zip.lexy.tgame.constants;
using zip.lexy.tgame.simulation.consumption;
using zip.lexy.tgame.state;
using zip.lexy.tgame.state.building;
using zip.lexy.tgame.state.city;
using zip.lexy.tgame.state.ship;
using zip.lexy.tgame.ui.gamegeneration;

namespace Smart_Trade.Sell_Logic
{
    internal class Sell_Logic
    {
        // --- SELL LOGIC ---
        // We iterate through all goods to ensure the TradeSheet is always populated 
        // with instructions, allowing the ship to sell goods it might acquire later.
        public static void SellLogic(CargoHolder _cargoSource,
            GameState _gameState,
            City _currentCity,
            List<City> _futureCities,
            PriceCalculator _priceCalc,
            List<(string good, int type, float score, int price, int tradeDepth)> _candidates)
        {
            foreach (string _good in Goods.ALL)
            {
                // Try to find the actual stack in cargo
                var cargoStack = _cargoSource.GetGoods().FirstOrDefault(g => g.type == _good);

                // Use the actual stack if it exists, otherwise create a virtual EMPTY one 
                // using the Core Price as the theoretical average cost.
                ItemStack _stack = cargoStack ?? new ItemStack(_good, 0f, _gameState.corePrices[_good]);

                // 1. Production/Consumption Check: Focus on Net Importers
                var prodDict = Production.GetCityProduction(_currentCity);
                float lProd = prodDict.ContainsKey(_good) ? prodDict[_good].amount : 0f;
                float lCons = Consumption.GetConsumption(_currentCity, _good);

                if (lCons <= lProd) continue;

                // 2. Market Depth: Use stack.averageCost (real or core baseline)
                float costToBeat = _stack.averageCost > 0 ? _stack.averageCost : _gameState.corePrices[_good];
                int profitableQty = PriceCalculator.CountGoodsBoughtByCityAbovePrice(_good, _currentCity, _gameState, (int)costToBeat);

                if (profitableQty <= 0) continue;

                // 3. Sample Size for Price: Use actual amount if available, otherwise 10 units for a quote
                int sampleSize = (_stack.amount > 1f) ? Mathf.Min(profitableQty, (int)_stack.amount) : Mathf.Min(profitableQty, 10);
                int sellTotal = _priceCalc.CityBuysGoods(_good, _currentCity, sampleSize);
                float sellPrice = (float)sellTotal / sampleSize;

                // 4. Scoring
                float score = Smart_Trade.Calculate_Trade_Score.Calculate_Trade_Score.CalculateTradeScore(_good, _currentCity, _futureCities, _gameState, _priceCalc);

                // If the ship doesn't actually have the items, reduce score priority 
                // so it doesn't block "Buy" opportunities, but remains active in the sheet.
                if (_stack.amount <= 0.1f) score *= 0.1f;

                _candidates.Add((_good, 2, score, Mathf.RoundToInt(sellPrice), profitableQty));
            }
        }
    }
}
