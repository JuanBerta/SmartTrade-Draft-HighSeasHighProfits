using Smart_Trade.Smart_Trade_Settings;
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
using zip.lexy.tgame.ui.gamegeneration;
using static SmartTradeDraft.SmartTradeDraftMod;

namespace Smart_Trade.Buy_Logic
{
    internal class Buy_Logic
    {
        // The Buy Logic is more conservative, only targeting goods with a clear surplus in the current city,
        // ensuring we don't buy items that are scarce locally.
        // We also check for actual stock availability at the desired price point to avoid creating unfulfillable trade instructions.
        // The scoring system remains consistent with the Sell Logic to maintain a unified decision-making framework.
        public static void BuyLogic(
    City _currentCity,
    GameState _gameState,
    List<City> _futureCities,
    PriceCalculator _priceCalc, // Fixed the extra parenthesis here
    List<(string good, int type, float _score, int price, int tradeDepth)> _candidates)
        {
            // --- BUY LOGIC ---
            foreach (string _goodName in Goods.ALL)
            {
                // 1. Production/Consumption Check
                var _prodDict = Production.GetCityProduction(_currentCity);
                float _lProd = _prodDict.ContainsKey(_goodName) ? _prodDict[_goodName].amount : 0f;
                float _lCons = Consumption.GetConsumption(_currentCity, _goodName);

                // We only buy if the city produces more than it consumes (surplus)
                if (_lProd <= _lCons) continue;

                // 2. Safety Check for corePrices
                if (!_gameState.corePrices.TryGetValue(_goodName, out float basePrice)) continue;

                // 3. Price Calculation
                float _buyMargin = Mathf.Clamp01(SmartTradeSettings.GetProfit());
                int _maxBuyPrice = Mathf.RoundToInt(basePrice * (1f - _buyMargin));

                // 4. Stock Availability
                int _availableCheapStock = PriceCalculator.CountGoodsSoldByCityBelowPrice(_goodName, _currentCity, _gameState, _maxBuyPrice);
                if (_availableCheapStock <= 0) continue;

                // 5. Profitability Score
                float _score = Smart_Trade.Calculate_Trade_Score.Calculate_Trade_Score.CalculateTradeScore(_goodName, _currentCity, _futureCities, _gameState, _priceCalc);
                if (_score <= 0) continue;

                // 6. Record Candidate (Type 1 = BUY)
                _candidates.Add((_goodName, 1, _score, _maxBuyPrice, _availableCheapStock));
            }
        }
    }
}
