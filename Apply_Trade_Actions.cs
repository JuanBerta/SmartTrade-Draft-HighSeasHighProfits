using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using zip.lexy.tgame.state.city;
using zip.lexy.tgame.state.ship.trading;

namespace Smart_Trade.Apply_Trade_Actions
{
    internal class Apply_Trade_Actions
    {
        // For each candidate, apply the trade actions by type 
        public static void ApplyTradeActions(
    List<(string good, int type, float score, int price, int tradeDepth)> _candidates,
    CityTradeSheet _currentSheet,
    City _currentCity,
    TradeRoute _tradeRoute)
        {
            // 1. Group by the 'good' name and pick the one with the highest 'score'
            var _bestPerGood = _candidates.GroupBy(c => c.good)
                .Select(g => g.OrderByDescending(x => x.score).First())
                .ToList();

            foreach (var c in _bestPerGood)
            {
                var _actionObject = _currentSheet.actions.FirstOrDefault(a => a.good == c.good);
                if (_actionObject == null) continue;

                // 2. Assign the candidate values to the game's action object
                _actionObject.type = c.type;
                _actionObject.priceAmount = c.price;
                _actionObject.active = true;

                if (c.type == 2) // SELL
                {
                    _actionObject.limitKeep = 0;
                }
                else // BUY (type == 1)
                {
                    float _currentCityAmount = _currentCity.goods[c.good].amount;
                    _actionObject.limitKeep = Mathf.Max(0, Mathf.RoundToInt(_currentCityAmount - c.tradeDepth));
                }

                // 3. Ensure the trade route knows this good is being handled
                if (!_tradeRoute.tradedGoods.Contains(c.good))
                {
                    _tradeRoute.tradedGoods.Add(c.good);
                }

                // 4. Feedback
                string tName = c.type == 1 ? "BUY" : "SELL";
                MelonLoader.MelonLogger.Msg($"[SmartTrade] {_currentCity.name}: {tName} {c.good} @ {c.price} (Depth: {c.tradeDepth})");
            }
        }
    }
}
