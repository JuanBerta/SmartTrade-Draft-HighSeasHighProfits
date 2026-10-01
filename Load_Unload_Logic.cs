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
using Smart_Trade.Smart_Trade_Settings;

namespace Smart_Trade.Load_Unload_Logic
{
    internal class Load_Unload_Logic
    {
        // LOAD(4)    = take surplus from the player's warehouse into the ship cargo.
        //               Used when the player's production of a good in this city exceeds their consumption by the configured margin.
        // DEPOSIT(3) = drop cargo into the player's warehouse.
        //               Used when the player's factories consume more of a good than they produce in this city.
        //
        // The game executes actions in the order DEPOSIT -> SELL -> LOAD -> BUY -> BALANCE,
        // so unloading supply for a shortage city runs before anything else, and loading surplus
        // runs before BUY. This gives load/unload automatic priority over regular trading and
        // prevents input goods from running out (which would stop player production).
        public static void LoadUnloadLogic(
            GameState _gameState,
            City _currentCity,
            CargoHolder _cargoSource,
            List<(string good, int type, float score, int price, int tradeDepth)> _candidates)
        {
            if (_gameState == null || _currentCity == null || _gameState.human == null) return;

            var playerProd = Production.GetPlayerProduction(_currentCity);
            var playerDemand = BusinessConsumption.GetHumanConsumption(_currentCity);
            float margin = SmartTradeSettings.GetLoadMargin();

            // Player warehouse of this city: source for LOAD, destination for DEPOSIT
            var warehouse = _currentCity.GetTraderWarehouse(_gameState.human);

            foreach (string good in Goods.ALL)
            {
                float prod = (playerProd != null && playerProd.TryGetValue(good, out var prodStack)) ? prodStack.amount : 0f;
                float cons = (playerDemand != null && playerDemand.TryGetValue(good, out var consStack)) ? consStack.amount : 0f;

                // Not part of the player's production chain in this city -> leave the action inactive
                if (prod <= 0f && cons <= 0f) continue;

                // --- SURPLUS: player produces more than it consumes (with margin) -> LOAD onto the ship ---
                if (prod > cons * (1f + margin))
                {
                    float surplus = prod - cons * (1f + margin);
                    float warehouseAmount = (warehouse != null && warehouse.GetGood(good) != null) ? warehouse.GetGood(good).amount : 0f;
                    if (warehouseAmount <= 0.1f) continue; // nothing available to load

                    int loadQty = Mathf.RoundToInt(Mathf.Min(surplus, warehouseAmount));
                    if (loadQty <= 0) continue;

                    // priceAmount = quantity to load, score = surplus size (bigger surplus gets more space first)
                    _candidates.Add((good, 4, surplus, loadQty, 0));
                }
                // --- DEFICIT: player consumes more than it produces (with margin) -> DEPOSIT cargo into the warehouse ---
                else if (cons > prod * (1f + margin))
                {
                    float deficit = cons - prod * (1f + margin);
                    float cargoAmount = (_cargoSource != null && _cargoSource.GetGood(good) != null) ? _cargoSource.GetGood(good).amount : 0f;
                    if (cargoAmount <= 0.1f) continue; // nothing to unload

                    int unloadQty = Mathf.RoundToInt(Mathf.Min(deficit, cargoAmount));
                    if (unloadQty <= 0) continue;

                    // priceAmount = quantity to deposit, score = deficit size (bigger shortage gets served first)
                    _candidates.Add((good, 3, deficit, unloadQty, 0));
                }
            }
        }
    }
}
