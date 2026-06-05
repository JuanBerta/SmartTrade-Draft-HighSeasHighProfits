using HarmonyLib;
using lexyvents;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using zip.lexy.tgame.constants;
using zip.lexy.tgame.events;
using zip.lexy.tgame.events.state;
using zip.lexy.tgame.saves;
using zip.lexy.tgame.simulation.consumption;
using zip.lexy.tgame.state;
using zip.lexy.tgame.state.building;
using zip.lexy.tgame.state.city;
using zip.lexy.tgame.state.ship;
using zip.lexy.tgame.state.ship.trading;
using zip.lexy.tgame.state.trader;
using zip.lexy.tgame.ui.gamegeneration;
using zip.lexy.tgame.ui.settings;
using zip.lexy.tgame.ui.widget.trade;
using zip.lexy.tgame.ui.widget.trader.auto;
using Smart_Trade.Process_Route_Sheets;
using Smart_Trade.Buy_Logic;
using Smart_Trade.Sell_Logic;
using Smart_Trade.Get_Class;
using Smart_Trade.Send_Melon_Logger_Message;
using Smart_Trade.Trade_Route_Loop;
using Smart_Trade.Recalculate_Trade_Route;
using Smart_Trade.Ensure_Dropdown;
using Smart_Trade.Try_Recalculate;
using Smart_Trade.Smart_Trade_Settings;
using Smart_Trade.Initialize_Trade_Sheet;

namespace SmartTradeDraft
{
    public class SmartTradeDraftMod : MelonMod
    {
        // Cache the field to save CPU cycles during frequent events
        private static FieldInfo _shipEventField;
        public override void OnInitializeMelon()
        {
            _shipEventField = typeof(OnShipCargoChanged).GetField("ship", BindingFlags.NonPublic | BindingFlags.Instance);
            HarmonyInstance.PatchAll();
        }

        [HarmonyPatch(typeof(AutoTradeRoutesWindow))]
        [HarmonyPatch("AddTradeSheet")]
        [HarmonyPatch(new System.Type[] { typeof(OnCityAddedToTradeRoute) })]
        public static class SmartTradeSetupPatch
        {
            public static void Postfix(AutoTradeRoutesWindow __instance, OnCityAddedToTradeRoute evt)
            {
                MelonLogger.Msg("[SmartTrade] Patch HIT");
                var gameState = Get_Class.GetGameState();
                var priceCalc = Get_Class.GetPriceCalculator();
                var ship = Get_Class.GetShip(gameState);
                var convoy = Get_Class.GetConvoy(ship);

                if (gameState == null || priceCalc == null || ship == null)
                {
                    Send_Melon_Logger_Message.SendMelonLoggerMessage($"[SmartTrade] Initialization failed: {gameState}, {priceCalc} or {ship} is null");
                    return;
                }

                Recalculate_Trade_Route.RecalculateTradeRoute(ship);

                if (convoy == null)
                {
                    MelonLogger.Msg($"{convoy} is null");
                    return;
                }
            }
        }

        [HarmonyPatch(typeof(EventBus), "Dispatch", new Type[] { typeof(object) })]
        public static class SmartTrade_CargoChangedPatch
        {

            public static void Postfix(object obj)
            {
                // Only act if the event is a cargo change
                if (!(obj is OnShipCargoChanged cargoEvt) || _shipEventField == null) return;

                var ship = _shipEventField.GetValue(cargoEvt) as Ship;
                if (ship == null) return;

                Try_Recalculate.TryRecalculate(ship);
            }
        }



        [HarmonyPatch(typeof(EventBus), "FireNow")]
        public static class SmartTrade_SeasonChangedPatch
        {
            public static void Postfix(object obj)
            {
                // Only trigger when the season transitions
                if (obj == null || obj.GetType().Name != "OnSeasonChanged") return;

                var gameState = Get_Class.GetGameState();
                var playerTrader = Get_Class.GetHuman(gameState);

                if (playerTrader == null || playerTrader.ships == null) return;

                Send_Melon_Logger_Message.SendMelonLoggerMessage("[SmartTrade] Season Changed: Syncing fleet strategy with new market conditions.");

                Trade_Route_Loop.TradeRouteLoop(playerTrader);
            }
        }

        [HarmonyPatch(typeof(GeneralSettingsWindow), "Show")]
        public static class GeneralSettings_UI_Injection_Patch
        {
            public static void Postfix(GeneralSettingsWindow __instance)
            {
                // Use the 'language' field directly from the instance to get the template
                // This is safer than transform.Find if the hierarchy changes
                Transform _languageTrasnform = __instance.transform.Find("window/language");
                Transform _windowTransform = __instance.transform.Find("window");

                if (_languageTrasnform == null || _windowTransform == null) return;

                // Create Buy Dropdown (Offset -70)
                Ensure_Dropdown.EnsureDropdown(__instance, _windowTransform, _languageTrasnform,
                    "smarttrade_buy_setting", "Smart Trade Desired Buy Profit %",
                    "smarttrade.profitMargin");
            }
        }

        //// For each candidate, group it by good and select the one with best scores
        //public static void ApplyBestActions(CityTradeSheet _sheet, List<(string good, int type, float score, int price)> _candidates, TradeRoute route)
        //{
        //    var bestPerGood = _candidates.GroupBy(c => c.good)
        //                                .Select(g => g.OrderByDescending(x => x.score).First());

        //    foreach (var c in bestPerGood)
        //    {
        //        var _action = _sheet.actions.FirstOrDefault(a => a.good == c.good);
        //        if (_action == null)
        //        {
        //            _action = new TradeAction { good = c.good };
        //            _sheet.actions.Add(_action);
        //        }

        //        _action.type = c.type;
        //        _action.priceAmount = c.price;
        //        _action.active = true;

        //        if (!route.tradedGoods.Contains(c.good)) route.tradedGoods.Add(c.good);
        //    }
        //}

        //// Helper methods must be STATIC and take parameters since they can't use 'this'
        //public static void LogProfitableSales(GameState _gameState,
        //    PriceCalculator _priceCalc,
        //    List<TradeWindowGood> _goods,
        //    int _tradeAmount)
        //{
        //    foreach (TradeWindowGood good in _goods)
        //    {
        //        // 1. Get the current city the player is looking at
        //        var city = _gameState.viewCity;

        //        // 2. Access the inventory from the selected ship or its convoy
        //        var ship = _gameState.selectedShip;
        //        if (ship == null || city == null) return;

        //        // Use the ship (which is a CargoHolder) to get the specific good
        //        var stack = ship.GetGood(good.GetGood());

        //        if (stack != null && stack.amount > 0)
        //        {
        //            int cityBuyPriceTotal = _priceCalc.CityBuysGoods(good.GetGood(), city, _tradeAmount);
        //            float currentUnitPrice = (float)cityBuyPriceTotal / _tradeAmount;

        //            // Note: Ensure your 'stack' object contains an 'averageCost' field
        //            if (currentUnitPrice > stack.averageCost)
        //            {
        //                Debug.Log($"Profit Alert: {good.GetGood()} is profitable at {city.name}.");
        //            }
        //        }
        //    }
        //}
    }
}