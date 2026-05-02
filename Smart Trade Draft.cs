using HarmonyLib;
using lexyvents;
using System;
using MelonLoader;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using zip.lexy.tgame.constants;
using zip.lexy.tgame.events;
using zip.lexy.tgame.events.state;
using zip.lexy.tgame.simulation.consumption;
using zip.lexy.tgame.state;
using zip.lexy.tgame.state.building;
using zip.lexy.tgame.state.city;
using zip.lexy.tgame.state.ship;
using zip.lexy.tgame.state.ship.trading;
using zip.lexy.tgame.ui.gamegeneration;
using zip.lexy.tgame.ui.settings;
using zip.lexy.tgame.ui.widget.trade;
using zip.lexy.tgame.ui.widget.trader.auto;

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
                var gameState = InstanceProvider.GetInstance<GameState>();
                var priceCalc = InstanceProvider.GetInstance<PriceCalculator>();
                var ship = gameState?.selectedShip;

                if (gameState == null || priceCalc == null || ship == null)
                {
                    MelonLogger.Msg($"{gameState}, {priceCalc} or {ship} is null");
                    return;
                }

                RecalculateTradeRoute(ship);

                var convoy = ship.convoy;
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
            static Dictionary<Ship, float> lastUpdate = new Dictionary<Ship, float>();

            public static void Postfix(object obj)
            {
                // Only act if the event is a cargo change
                if (!(obj is OnShipCargoChanged cargoEvt) || _shipEventField == null) return;

                var ship = _shipEventField.GetValue(cargoEvt) as Ship;
                if (ship == null) return;

                TryRecalculate(ship);
            }

            static void TryRecalculate(Ship ship)
            {
                float now = Time.time;
                if (lastUpdate.TryGetValue(ship, out float last))
                {
                    // Throttling: Don't recalculate more than once per second per ship
                    if (now - last < 1.0f) return;
                }

                lastUpdate[ship] = now;
                RecalculateTradeRoute(ship);
            }
        }

        public static void RecalculateTradeRoute(Ship ship)
        {
            var gameState = InstanceProvider.GetInstance<GameState>();
            var priceCalc = InstanceProvider.GetInstance<PriceCalculator>();

            if (gameState == null || priceCalc == null || ship?.convoy?.tradeRoute == null) return;

            var convoy = ship.convoy;
            if (convoy?.tradeRoute == null)
                return;

            var tradeRoute = convoy.tradeRoute;
            var routeCities = tradeRoute.tradeSheets
            .Select(s => gameState.cities[s.cityId])
            .ToList();

            for (int i = 0; i < tradeRoute.tradeSheets.Count; i++)
            {
                var currentSheet = tradeRoute.tradeSheets[i];
                var currentCity = gameState.cities[currentSheet.cityId];


                var futureCities = routeCities.Skip(i + 1).ToList();
                bool isLastCity = futureCities.Count == 0;

                // Ensure goods exist in THIS sheet
                foreach (string good in Goods.ALL)
                {
                    var existingGoods = new HashSet<string>(currentSheet.actions.Select(a => a.good));
                    if (!existingGoods.Contains(good))
                    {
                        currentSheet.actions.Add(new TradeAction
                        {
                            good = good,
                            type = 0,
                            active = false
                        });
                    }
                }

                var candidates = new List<(string good, int type, float score, int price)>();

                CargoHolder cargoSource = (CargoHolder)convoy;


                // 🔥 Clear previous auto decisions (important)
                foreach (var action in currentSheet.actions)
                {
                    action.active = false;
                }
                
                // --- SELL (REAL + PREDICTED) ---

                bool hasCargo = cargoSource.GetGoods().Any(g => g.amount > 0.1f);

                if (hasCargo)
                {
                    // ✅ REAL SELL (existing logic)
                    // ✅ REAL SELL (Updated with Production check)
                    foreach (var stack in cargoSource.GetGoods())
                    {
                        if (stack.amount <= 0.1f) continue;

                        string good = stack.type;
                        var productionDict = Production.GetCityProduction(currentCity);

                        // Check if the city produces this good
                        bool isNotProducedLocally = !productionDict.ContainsKey(good) || productionDict[good].amount <= 0f;

                        float realCost = stack.averageCost > 0 ? stack.averageCost : gameState.corePrices[good];
                        int sellTotal = priceCalc.CityBuysGoods(good, currentCity, 10);
                        if (sellTotal <= 0) continue;

                        float sellPrice = (float)sellTotal / 10;
                        float profit = sellPrice - realCost;
                        float minProfit = SmartTradeSettings.GetSellProfit();

                        // Even if profit is low, if they don't produce it, we might still want to dump it
                        // to free up cargo space, but let's stick to your profit requirement for now:
                        if (profit - (realCost * minProfit) <= 0 && !isNotProducedLocally)
                            continue;

                        float pressure = GetMarketPressure(currentCity, good);
                        float inventoryFactor = Mathf.Clamp(stack.amount / 50f, 1f, 3f);

                        // 🔥 NEW: Add a multiplier if not produced locally
                        float productionMultiplier = isNotProducedLocally ? 1.5f : 1.0f;

                        float score = profit * stack.amount * inventoryFactor * (1f + Mathf.Max(0, pressure)) * productionMultiplier;

                        int targetSellPrice = Mathf.RoundToInt(sellPrice * (1f + minProfit));
                        candidates.Add((good, TradeAction.SELL, score, targetSellPrice));
                    }
                }
                else
                {
                    foreach (string good in Goods.ALL)
                    {
                        float bestProfit = 0f;
                        float bestBuyPrice = 0f;

                        if (!isLastCity)
                        {
                            // NORMAL (existing logic)
                            foreach (var futureCity in futureCities)
                            {
                                int buyTotal = priceCalc.CitySellsGoods(good, currentCity, 10);
                                int sellTotal = priceCalc.CityBuysGoods(good, futureCity, 10);

                                if (buyTotal <= 0 || sellTotal <= 0)
                                    continue;

                                float buyPrice = (float)buyTotal / 10;
                                float sellPrice = (float)sellTotal / 10;

                                float profit = sellPrice - buyPrice;

                                if (profit > bestProfit)
                                {
                                    bestProfit = profit;
                                    bestBuyPrice = buyPrice;
                                }
                            }
                        }
                        else
                        {
                            // 🔥 LAST CITY LOGIC (NEW)
                            int sellTotal = priceCalc.CityBuysGoods(good, currentCity, 10);
                            if (sellTotal <= 0)
                                continue;

                            float sellPrice = (float)sellTotal / 10;

                            float basePrice = gameState.corePrices[good];

                            bestProfit = sellPrice - basePrice;
                            bestBuyPrice = basePrice;
                        }

                        float minProfit = SmartTradeSettings.GetSellProfit();

                        // Check production at the target destination (if isLastCity, it's currentCity)
                        var targetCity = isLastCity ? currentCity : futureCities.First(); // Simplification
                        var targetProd = Production.GetCityProduction(targetCity);
                        bool isNotProducedAtTarget = !targetProd.ContainsKey(good) || targetProd[good].amount <= 0f;

                        if (bestProfit <= bestBuyPrice * minProfit && !isNotProducedAtTarget)
                            continue;

                        float pressure = GetMarketPressure(currentCity, good);

                        // 🔥 NEW: Boost the score for goods that are in high demand (not produced)
                        float productionMultiplier = isNotProducedAtTarget ? 1.3f : 1.0f;
                        float score = bestProfit * (1f + Mathf.Max(0, pressure)) * productionMultiplier;

                        int targetSellPrice = Mathf.RoundToInt((bestBuyPrice + bestProfit) * (1f + minProfit));
                        candidates.Add((good, TradeAction.SELL, score, targetSellPrice));
                    }
                }

                // --- BUY ---
                foreach (string goodName in Goods.ALL)
                {
                    var productionDict = Production.GetCityProduction(currentCity);

                    // 🚫 Skip goods that are not produced
                    if (!productionDict.ContainsKey(goodName) || productionDict[goodName].amount <= 0f)
                        continue;

                    float score = CalculateTradeScore(
                        goodName,
                        currentCity,
                        futureCities,
                        gameState,
                        priceCalc);

                    if (score <= 0)
                        continue;

                    float buyMargin = Mathf.Clamp01(SmartTradeSettings.GetBuyProfit());

                    int targetBuyPrice = Mathf.RoundToInt(
                        gameState.corePrices[goodName] * (1f - buyMargin)
                    );

                    candidates.Add((
                        goodName,
                        TradeAction.BUY,
                        score,
                        targetBuyPrice
                    ));
                }

                if (candidates.Count == 0)
                {
                    // Fix 1: Changed 'i' to 'j' to avoid conflict with the outer loop
                    for (int j = 0; j < tradeRoute.tradeSheets.Count; j++)
                    {
                        // Fix 2: Added 'var' or type to ensure currentSheet is locally defined if needed
                        var targetSheet = tradeRoute.tradeSheets[j];

                        if (targetSheet.actions.Count < Goods.ALL.Count)
                        {
                            InitializeTradeSheet(targetSheet);
                        }

                        // Fix 3: You need to iterate through goods here to use 'good'
                        foreach (string goodKey in Goods.ALL)
                        {
                            int sellTotal = priceCalc.CityBuysGoods(goodKey, currentCity, 10);
                            if (sellTotal <= 0)
                                continue;

                            float sellPrice = (float)sellTotal / 10;

                            candidates.Add((
                                goodKey,
                                TradeAction.SELL,
                                0.01f, // very low priority
                                Mathf.RoundToInt(sellPrice)
                            ));
                        }
                    }
                }

                // --- Apply best ---
                var bestPerGood = candidates
                    .GroupBy(c => c.good)
                    .Select(g =>
                    {
                        var best = g.OrderByDescending(x => x.score).First();

                        var sell = g.FirstOrDefault(x => x.type == TradeAction.SELL);
                        if (sell.good != null && sell.score > best.score * 0.8f)
                            return sell;

                        return best;
                    })
                    .ToList();

                foreach (var c in bestPerGood)
                {
                    var action = currentSheet.actions.First(a => a.good == c.good);

                    action.type = c.type;
                    action.priceAmount = c.price;
                    action.active = true;

                    // Register globally (needed for UI)
                    if (!tradeRoute.tradedGoods.Contains(c.good))
                    {
                        tradeRoute.tradedGoods.Add(c.good);
                    }

                    MelonLogger.Msg($"[SmartTrade] {currentCity.name}: {c.type} {c.good}");
                }
            }
        }

        public static void InitializeTradeSheet(CityTradeSheet sheet)
        {
            var existingGoods = new HashSet<string>(sheet.actions.Select(a => a.good));
            foreach (string good in Goods.ALL)
            {
                if (!existingGoods.Contains(good))
                {
                    sheet.actions.Add(new TradeAction
                    {
                        good = good,
                        type = 0,
                        active = false
                    });
                }
            }
        }

        public static void ApplyBestActions(CityTradeSheet sheet, List<(string good, int type, float score, int price)> candidates, TradeRoute route)
        {
            var bestPerGood = candidates.GroupBy(c => c.good)
                                        .Select(g => g.OrderByDescending(x => x.score).First());

            foreach (var c in bestPerGood)
            {
                var action = sheet.actions.FirstOrDefault(a => a.good == c.good);
                if (action == null)
                {
                    action = new TradeAction { good = c.good };
                    sheet.actions.Add(action);
                }

                action.type = c.type;
                action.priceAmount = c.price;
                action.active = true;

                if (!route.tradedGoods.Contains(c.good)) route.tradedGoods.Add(c.good);
            }
        }

        [HarmonyPatch(typeof(GeneralSettingsWindow), "Show")]
        public static class GeneralSettings_UI_Injection_Patch
        {
            public static void Postfix(GeneralSettingsWindow __instance)
            {
                // Use the 'language' field directly from the instance to get the template
                // This is safer than transform.Find if the hierarchy changes
                Transform languageTransform = __instance.transform.Find("window/language");
                Transform windowTransform = __instance.transform.Find("window");

                if (languageTransform == null || windowTransform == null) return;

                // Create Buy Dropdown (Offset -70)
                EnsureDropdown(__instance, windowTransform, languageTransform,
                    "smarttrade_buy_setting", "Smart Trade Desired Buy Profit %",
                    "smarttrade.buyMargin");

                // Create Sell Dropdown (Offset -140)
                EnsureDropdown(__instance, windowTransform, languageTransform,
                    "smarttrade_sell_setting", "Smart Trade Desired Sell Profit %",
                    "smarttrade.sellMargin");
            }

            private static void EnsureDropdown(GeneralSettingsWindow __instance, Transform parent, Transform template, string name, string labelText, string prefKey)
            {
                // Check if it already exists so we don't duplicate on every 'Show'
                Transform existing = parent.Find(name);
                if (existing != null) return;

                // 1. Clone the row
                GameObject row = UnityEngine.Object.Instantiate(template.gameObject, parent);
                row.name = name;

                // 2. Setup the Label
                TMPro.TextMeshProUGUI label = row.transform.Find("label").GetComponent<TMPro.TextMeshProUGUI>();
                label.text = labelText;

                // 3. Setup the Dropdown
                TMPro.TMP_Dropdown dropdown = row.transform.Find("dropdown").GetComponent<TMPro.TMP_Dropdown>();

                // Clear the original 'ChangeLanguage' listeners
                dropdown.onValueChanged = new TMPro.TMP_Dropdown.DropdownEvent();
                dropdown.options.Clear();

                // 4. Fill Options (0% to 50% in steps of 5)
                List<int> values = new List<int> { 0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50 };
                foreach (int v in values)
                {
                    dropdown.options.Add(new TMPro.TMP_Dropdown.OptionData { text = $"{v}%" });
                }

                // 5. Load Saved Value
                int currentSaved = PlayerPrefs.GetInt(prefKey, 10); // Default 10%
                int index = values.IndexOf(currentSaved);
                dropdown.SetValueWithoutNotify(index != -1 ? index : 2); // Default to index 2 (10%) if not found

                // 6. Save Logic
                dropdown.onValueChanged.AddListener((int val) =>
                {
                    int selectedValue = values[val];
                    PlayerPrefs.SetInt(prefKey, selectedValue);
                    MelonLoader.MelonLogger.Msg($"{labelText} updated to: {selectedValue}%");
                });
            }
        }

        // Helper methods must be STATIC and take parameters since they can't use 'this'
        public static void LogProfitableSales(GameState gameState, PriceCalculator priceCalc, List<TradeWindowGood> goods, int tradeAmount)
        {
            foreach (TradeWindowGood good in goods)
            {
                // 1. Get the current city the player is looking at
                var city = gameState.viewCity;

                // 2. Access the inventory from the selected ship or its convoy
                var ship = gameState.selectedShip;
                if (ship == null || city == null) return;

                // Use the ship (which is a CargoHolder) to get the specific good
                var stack = ship.GetGood(good.GetGood());

                if (stack != null && stack.amount > 0)
                {
                    int cityBuyPriceTotal = priceCalc.CityBuysGoods(good.GetGood(), city, tradeAmount);
                    float currentUnitPrice = (float)cityBuyPriceTotal / tradeAmount;

                    // Note: Ensure your 'stack' object contains an 'averageCost' field
                    if (currentUnitPrice > stack.averageCost)
                    {
                        Debug.Log($"Profit Alert: {good.GetGood()} is profitable at {city.name}.");
                    }
                }
            }
        }

        // --- MARKET ANALYSIS ---

        static float GetMarketPressure(City city, string good)
        {
            var productionDict = Production.GetCityProduction(city);
            var consumptionDict = Consumption.GetCityConsumption(city);

            float production = productionDict.ContainsKey(good) ? productionDict[good].amount : 0f;

            float consumption = consumptionDict.ContainsKey(good)
                ? consumptionDict[good].amount
                : 0f;

            float imbalance = consumption - production;
            float total = production + consumption + 1f;

            return imbalance / total;
        }

        // --- SCORING SYSTEM ---
        static float CalculateTradeScore(
            string good,
            City currentCity,
            List<City> futureCities,
            GameState gameState,
            PriceCalculator priceCalc)
        {
            float bestScore = 0f;

            float sourcePressure = GetMarketPressure(currentCity, good);

            // Skip non-production goods (huge optimization)
            if (sourcePressure > -0.05f)
                return 0f;

            for (int amount = 10; amount <= 100; amount += 10)
            {
                int totalBuy = priceCalc.CitySellsGoods(good, currentCity, amount);
                if (totalBuy <= 0) continue;

                float buyPrice = (float)totalBuy / amount;

                foreach (var futureCity in futureCities)
                {
                    int totalSell = priceCalc.CityBuysGoods(good, futureCity, amount);
                    if (totalSell <= 0) continue;

                    float sellPrice = (float)totalSell / amount;
                    float profitPerUnit = sellPrice - buyPrice;

                    float targetPressure = GetMarketPressure(futureCity, good);

                    float pressureFactor = 1f;

                    if (sourcePressure < 0)
                        pressureFactor += Mathf.Abs(sourcePressure);

                    if (targetPressure > 0)
                        pressureFactor += targetPressure;

                    float minProfit = SmartTradeSettings.GetBuyProfit();

                    if (profitPerUnit <= buyPrice * minProfit)
                        continue;

                    float score = profitPerUnit * amount * pressureFactor;

                    if (score > bestScore)
                        bestScore = score;
                }
            }

            return bestScore;
        }

        public static class SmartTradeSettings
        {
            public const string BUY_PROFIT_KEY = "smarttrade.buyMargin";
            public const string SELL_PROFIT_KEY = "smarttrade.sellMargin";

            public static float GetBuyProfit() =>
                PlayerPrefs.GetFloat(BUY_PROFIT_KEY, 0.1f); // default 10%

            public static float GetSellProfit() =>
                PlayerPrefs.GetFloat(SELL_PROFIT_KEY, 0.05f);
        }
    }
}