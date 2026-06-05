using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using zip.lexy.tgame.state;
using zip.lexy.tgame.state.city;
using zip.lexy.tgame.ui.gamegeneration;
using Smart_Trade.Get_Class;
using Smart_Trade.Smart_Trade_Settings;

namespace Smart_Trade.Calculate_Trade_Score
{
    internal class Calculate_Trade_Score
    {
        // --- SCORING SYSTEM ---
        public static float CalculateTradeScore(
            string _good,
            City _currentCity,
            List<City> _futureCities,
            GameState _gameState,
            PriceCalculator _priceCalc)
        {
            float bestScore = 0f;

            float sourcePressure = Get_Class.Get_Class.GetMarketPressure(_currentCity, _good);

            // Skip non-production _goods (huge optimization)
            if (sourcePressure > -0.05f)
                return 0f;

            for (int amount = 10; amount <= 100; amount += 10)
            {
                int totalBuy = _priceCalc.CitySellsGoods(_good, _currentCity, amount);
                if (totalBuy <= 0) continue;

                float buyPrice = (float)totalBuy / amount;

                foreach (var futureCity in _futureCities)
                {
                    int totalSell = _priceCalc.CityBuysGoods(_good, futureCity, amount);
                    if (totalSell <= 0) continue;

                    float sellPrice = (float)totalSell / amount;
                    float profitPerUnit = sellPrice - buyPrice;

                    float targetPressure = Get_Class.Get_Class.GetMarketPressure(futureCity, _good);

                    float pressureFactor = 1f;

                    if (sourcePressure < 0)
                        pressureFactor += Mathf.Abs(sourcePressure);

                    if (targetPressure > 0)
                        pressureFactor += targetPressure;

                    float minProfit = SmartTradeSettings.GetProfit();

                    if (profitPerUnit <= buyPrice * minProfit)
                        continue;

                    float score = profitPerUnit * amount * pressureFactor;

                    if (score > bestScore)
                        bestScore = score;
                }
            }

            return bestScore;
        }
    }
}
