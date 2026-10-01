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
        // Game's native action execution order: DEPOSIT(3) -> SELL(2) -> LOAD(4) -> BUY(1) -> BALANCE(5).
        // We reuse it as the type-priority so supply-first decisions (unload shortage / move surplus)
        // win over plain profit trades when the same good has competing candidates.
        private static int GetActionPriority(int type)
        {
            return type == 3 ? 5 : type == 2 ? 4 : type == 4 ? 3 : type == 1 ? 2 : type == 5 ? 1 : 0;
        }

        // For each candidate, apply the trade actions by type 
        public static void ApplyTradeActions(
    List<(string good, int type, float score, int price, int tradeDepth)> _candidates,
    CityTradeSheet _currentSheet,
    City _currentCity,
    TradeRoute _tradeRoute)
        {
            // 1. Group by the 'good' name and pick the best candidate:
            //    highest type priority first (game order), then highest score within the same type.
            var _bestPerGood = _candidates.GroupBy(c => c.good)
                .Select(g => g.OrderByDescending(c => GetActionPriority(c.type)).ThenByDescending(x => x.score).First())
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
                else if (c.type == 1) // BUY
                {
                    float _currentCityAmount = _currentCity.goods[c.good].amount;
                    _actionObject.limitKeep = Mathf.Max(0, Mathf.RoundToInt(_currentCityAmount - c.tradeDepth));
                }
                else // LOAD (4) / DEPOSIT (3): keep nothing back so surpluses are moved and shortages fully served
                {
                    _actionObject.limitKeep = 0;
                }

                // 3. Ensure the trade route knows this good is being handled
                if (!_tradeRoute.tradedGoods.Contains(c.good))
                {
                    _tradeRoute.tradedGoods.Add(c.good);
                }

                // 4. Feedback
                // NOTE: Disabled to avoid console spam during per-second route recalculation (performance issue).
                // Re-enable by uncommenting if detailed trade feedback is needed.
                // string tName = c.type == 1 ? "BUY" : c.type == 2 ? "SELL" : c.type == 3 ? "DEPOSIT" : c.type == 4 ? "LOAD" : "?";
                // MelonLoader.MelonLogger.Msg($"[SmartTrade] {_currentCity.name}: {tName} {c.good} @ {c.price} (Depth: {c.tradeDepth})");
            }
        }
    }
}
