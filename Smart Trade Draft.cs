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



        [HarmonyPatch(typeof(EventBus), "FireNow")]
        public static class SmartTrade_SeasonChangedPatch
        {
            public static void Postfix(object obj)
            {
                // Only trigger when the season transitions
                if (obj == null || obj.GetType().Name != "OnSeasonChanged") return;

                var gameState = InstanceProvider.GetInstance<GameState>();
                var playerTrader = gameState?.human; // Using your 'human' fix

                if (playerTrader == null || playerTrader.ships == null) return;

                MelonLoader.MelonLogger.Msg("[SmartTrade] Season Changed: Syncing fleet strategy with new market conditions.");

                foreach (var ship in playerTrader.ships)
                {
                    if (ship?.convoy?.tradeRoute != null && ship.convoy.tradeRoute.active)
                    {
                        RecalculateTradeRoute(ship);
                    }
                }
            }
        }

        public static void RecalculateTradeRoute(Ship ship)
        {
            var gameState = InstanceProvider.GetInstance<GameState>();
            var priceCalc = InstanceProvider.GetInstance<PriceCalculator>();

            if (gameState == null || priceCalc == null || ship?.convoy?.tradeRoute == null) return;

            var convoy = ship.convoy;
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

                foreach (string good in Goods.ALL)
                {
                    var existingGoods = new HashSet<string>(currentSheet.actions.Select(a => a.good));
                    if (!existingGoods.Contains(good))
                    {
                        currentSheet.actions.Add(new TradeAction { good = good, type = 0, active = false });
                    }
                }

                var candidates = new List<(string good, int type, float score, int price, int tradeDepth)>();
                CargoHolder cargoSource = (CargoHolder)convoy;

                foreach (var action in currentSheet.actions) { action.active = false; }

                // --- SELL LOGIC ---
                // We iterate through all goods to ensure the TradeSheet is always populated 
                // with instructions, allowing the ship to sell goods it might acquire later.
                foreach (string good in Goods.ALL)
                {
                    // Try to find the actual stack in cargo
                    var cargoStack = cargoSource.GetGoods().FirstOrDefault(g => g.type == good);

                    // Use the actual stack if it exists, otherwise create a virtual EMPTY one 
                    // using the Core Price as the theoretical average cost.
                    ItemStack stack = cargoStack ?? new ItemStack(good, 0f, gameState.corePrices[good]);

                    // 1. Production/Consumption Check: Focus on Net Importers
                    var prodDict = Production.GetCityProduction(currentCity);
                    float lProd = prodDict.ContainsKey(good) ? prodDict[good].amount : 0f;
                    float lCons = Consumption.GetConsumption(currentCity, good);

                    if (lCons <= lProd) continue;

                    // 2. Market Depth: Use stack.averageCost (real or core baseline)
                    float costToBeat = stack.averageCost > 0 ? stack.averageCost : gameState.corePrices[good];
                    int profitableQty = PriceCalculator.CountGoodsBoughtByCityAbovePrice(good, currentCity, gameState, (int)costToBeat);

                    if (profitableQty <= 0) continue;

                    // 3. Sample Size for Price: Use actual amount if available, otherwise 10 units for a quote
                    int sampleSize = (stack.amount > 1f) ? Mathf.Min(profitableQty, (int)stack.amount) : Mathf.Min(profitableQty, 10);
                    int sellTotal = priceCalc.CityBuysGoods(good, currentCity, sampleSize);
                    float sellPrice = (float)sellTotal / sampleSize;

                    // 4. Scoring
                    float score = CalculateTradeScore(good, currentCity, futureCities, gameState, priceCalc);

                    // If the ship doesn't actually have the items, reduce score priority 
                    // so it doesn't block "Buy" opportunities, but remains active in the sheet.
                    if (stack.amount <= 0.1f) score *= 0.1f;

                    candidates.Add((good, 2, score, Mathf.RoundToInt(sellPrice), profitableQty));
                }

                // --- BUY LOGIC ---
                foreach (string goodName in Goods.ALL)
                {
                    var prodDict = Production.GetCityProduction(currentCity);
                    float lProd = prodDict.ContainsKey(goodName) ? prodDict[goodName].amount : 0f;
                    float lCons = Consumption.GetConsumption(currentCity, goodName);

                    if (lProd <= lCons) continue;

                    // How many items are "cheap" enough to buy?
                    float buyMargin = Mathf.Clamp01(SmartTradeSettings.GetBuyProfit());
                    int maxBuyPrice = Mathf.RoundToInt(gameState.corePrices[goodName] * (1f - buyMargin));

                    int availableCheapStock = PriceCalculator.CountGoodsSoldByCityBelowPrice(goodName, currentCity, gameState, maxBuyPrice);
                    if (availableCheapStock <= 0) continue;

                    float score = CalculateTradeScore(goodName, currentCity, futureCities, gameState, priceCalc);
                    if (score <= 0) continue;

                    candidates.Add((goodName, 1, score, maxBuyPrice, availableCheapStock));
                }

                // --- APPLY BEST ACTIONS ---
                var bestPerGood = candidates.GroupBy(c => c.good)
                    .Select(g => g.OrderByDescending(x => x.score).First())
                    .ToList();

                foreach (var c in bestPerGood)
                {
                    var actionObject = currentSheet.actions.FirstOrDefault(a => a.good == c.good);
                    if (actionObject == null) continue;

                    actionObject.type = c.type;
                    actionObject.priceAmount = c.price;
                    actionObject.active = true;

                    if (c.type == 2) // SELL
                    {
                        // Sell until city stock reaches a level where they won't pay our price anymore
                        actionObject.limitKeep = 0;
                    }
                    else // BUY
                    {
                        // Set the city stock limit. If city has 100 and we want to buy 20 cheap ones, 
                        // we set limit to 80 so the ship buys until 80 are left.
                        float currentCityAmount = currentCity.goods[c.good].amount;
                        actionObject.limitKeep = Mathf.Max(0, Mathf.RoundToInt(currentCityAmount - c.tradeDepth));
                    }

                    if (!tradeRoute.tradedGoods.Contains(c.good)) tradeRoute.tradedGoods.Add(c.good);

                    string tName = c.type == 1 ? "BUY" : "SELL";
                    MelonLoader.MelonLogger.Msg($"[SmartTrade] {currentCity.name}: {tName} {c.good} @ {c.price} (Depth: {c.tradeDepth})");
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