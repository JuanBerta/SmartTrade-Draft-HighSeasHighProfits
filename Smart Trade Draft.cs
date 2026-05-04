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
                var gameState = GetGameState();
                var priceCalc = GetPriceCalculator();
                var ship = GetShip(gameState);
                var convoy = GetConvoy(ship);

                if (gameState == null || priceCalc == null || ship == null)
                {
                    SendMelonLoggerMessage($"[SmartTrade] Initialization failed: {gameState}, {priceCalc} or {ship} is null");
                    return;
                }

                RecalculateTradeRoute(ship);

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

                var gameState = GetGameState();
                var playerTrader = GetHuman(gameState);

                if (playerTrader == null || playerTrader.ships == null) return;

                SendMelonLoggerMessage("[SmartTrade] Season Changed: Syncing fleet strategy with new market conditions.");

                TradeRouteLoop(playerTrader);
            }
        }

        static Ship GetShip(GameState _gameState)
        {
            return _gameState?.selectedShip;
        }

        static Human GetHuman(GameState _gameState)
        {
            return _gameState.human;
        }

        static void SendMelonLoggerMessage(string message)
        {
            MelonLogger.Msg($"{message}");
        }

        // This method iterates through all the player's ships and recalculates their trade routes if they have an active one.
        // It's called on season changes to adapt to new market conditions.
        static void TradeRouteLoop(Human _playerTrader)
        {
            foreach (var ship in _playerTrader.ships)
            {
                if (ship?.convoy?.tradeRoute != null && ship.convoy.tradeRoute.active)
                {
                    RecalculateTradeRoute(ship);
                }
            }
        }

        // This is the main method that orchestrates the entire recalculation process for a ship's trade route.
        // It gathers all necessary data and then delegates to a helper method that processes each trade sheet in the route.
        public static void RecalculateTradeRoute(Ship _ship)
        {
            var _gameState = GetGameState();
            var _priceCalc = GetPriceCalculator();

            if (_gameState == null || _priceCalc == null || GetTradeRoute(_ship) == null) return;

            var _convoy = GetConvoy(_ship);
            var _tradeRoute = GetTradeRoute(_ship);
            var _routeCities = GetCurrentSheet(_tradeRoute, _gameState);

            List<(string, int, float, int, int)> _candidates = GetCandidate();
            var _cargoSource = GetCargoHolder(_convoy);

            // Now the main method is just a few clear steps
            ProcessRouteSheets(_tradeRoute, _routeCities, _gameState, _priceCalc, _cargoSource, _candidates);
        }

        // This method encapsulates the entire logic for processing the trade sheets of a route, making it easier to read and maintain.
        private static void ProcessRouteSheets(
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
                AddActions(currentSheet);
                DisableActions(currentSheet);

                // --- STEP 2: GENERATE POSSIBILITIES ---
                SellLogic(_cargoSource, _gameState, currentCity, futureCities, _priceCalc, _candidates);
                BuyLogic(currentCity, _gameState, futureCities, _priceCalc, _candidates);

                // --- STEP 3: EXECUTE BEST CHOICE ---
                ApplyTradeActions(_candidates, currentSheet, currentCity, _tradeRoute);
            }
        }

        // We access the GameState directly from the InstanceProvider, which is more efficient than trying to find it through the player's fleet or trade routes,
        static GameState GetGameState()
        {
            return InstanceProvider.GetInstance<GameState>();
        }

        // We access the PriceCalculator directly from the InstanceProvider,
        // which is more efficient than trying to find it through the player's fleet or trade routes,
        static PriceCalculator GetPriceCalculator()
        {
            return InstanceProvider.GetInstance<PriceCalculator>();
        }

        // We access the Convoy directly from the ship, which is more efficient than searching through the player's fleet,
        static Convoy GetConvoy(Ship _ship)
        {
            return _ship?.convoy;
        }

        // We get the TradeRoute directly from the ship's convoy, which is more efficient than searching through the player's trade routes,
        // especially since we're already operating on a ship that has an active route.
        static TradeRoute GetTradeRoute(Ship _ship)
        {
            return _ship?.convoy?.tradeRoute;
        }

        static List<City> GetCurrentSheet(TradeRoute _tradeRoute, GameState _gameState)
        {
            return new List<CityTradeSheet>(_tradeRoute.tradeSheets).Select(s => _gameState.cities[s.cityId])
                .ToList();
        }

        // We ensure that every TradeSheet has an action entry for every good, even if it's inactive,
        // to maintain a consistent interface and allow the ship to reactivate trades as it acquires new goods.
        static void AddActions(CityTradeSheet _currentSheet)
        {
            foreach (string good in Goods.ALL)
            {
                var existingGoods = new HashSet<string>(_currentSheet.actions.Select(a => a.good));
                if (!existingGoods.Contains(good))
                {
                    _currentSheet.actions.Add(new TradeAction { good = good, type = 0, active = false });
                }
            }
        }

        // We use a tuple list to store candidate trades with all necessary info for decision-making and application.
        static List<(string, int, float, int, int)> GetCandidate()
        {
            return new List<(string good, int type, float score, int price, int tradeDepth)>();
        }

        // Since Convoy implements CargoHolder, we can directly cast it to access the cargo without needing to find the ship or player.
        static CargoHolder GetCargoHolder(Convoy _convoy)
        {
            CargoHolder _cargoSource = (CargoHolder)_convoy;
            return _cargoSource;
        }

        // We disable all actions at the start of each recalculation to ensure a clean slate, allowing our logic to reactivate only the most optimal trades.
        static void DisableActions(CityTradeSheet _currentSheet)
        {
            foreach (var _action in _currentSheet.actions) { _action.active = false; }
        }

        // --- SELL LOGIC ---
        // We iterate through all goods to ensure the TradeSheet is always populated 
        // with instructions, allowing the ship to sell goods it might acquire later.
        static void SellLogic(CargoHolder _cargoSource,
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
                float score = CalculateTradeScore(_good, _currentCity, _futureCities, _gameState, _priceCalc);

                // If the ship doesn't actually have the items, reduce score priority 
                // so it doesn't block "Buy" opportunities, but remains active in the sheet.
                if (_stack.amount <= 0.1f) score *= 0.1f;

                _candidates.Add((_good, 2, score, Mathf.RoundToInt(sellPrice), profitableQty));
            }
        }

        // The Buy Logic is more conservative, only targeting goods with a clear surplus in the current city,
        // ensuring we don't buy items that are scarce locally.
        // We also check for actual stock availability at the desired price point to avoid creating unfulfillable trade instructions.
        // The scoring system remains consistent with the Sell Logic to maintain a unified decision-making framework.
        static void BuyLogic(
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
                float _buyMargin = Mathf.Clamp01(SmartTradeSettings.GetBuyProfit());
                int _maxBuyPrice = Mathf.RoundToInt(basePrice * (1f - _buyMargin));

                // 4. Stock Availability
                int _availableCheapStock = PriceCalculator.CountGoodsSoldByCityBelowPrice(_goodName, _currentCity, _gameState, _maxBuyPrice);
                if (_availableCheapStock <= 0) continue;

                // 5. Profitability Score
                float _score = CalculateTradeScore(_goodName, _currentCity, _futureCities, _gameState, _priceCalc);
                if (_score <= 0) continue;

                // 6. Record Candidate (Type 1 = BUY)
                _candidates.Add((_goodName, 1, _score, _maxBuyPrice, _availableCheapStock));
            }
        }

        static void ApplyTradeActions(
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


        public static void InitializeTradeSheet(CityTradeSheet _sheet)
        {
            var existingGoods = new HashSet<string>(_sheet.actions.Select(a => a.good));
            foreach (string good in Goods.ALL)
            {
                if (!existingGoods.Contains(good))
                {
                    _sheet.actions.Add(new TradeAction
                    {
                        good = good,
                        type = 0,
                        active = false
                    });
                }
            }
        }

        public static void ApplyBestActions(CityTradeSheet _sheet, List<(string good, int type, float score, int price)> _candidates, TradeRoute route)
        {
            var bestPerGood = _candidates.GroupBy(c => c.good)
                                        .Select(g => g.OrderByDescending(x => x.score).First());

            foreach (var c in bestPerGood)
            {
                var _action = _sheet.actions.FirstOrDefault(a => a.good == c.good);
                if (_action == null)
                {
                    _action = new TradeAction { good = c.good };
                    _sheet.actions.Add(_action);
                }

                _action.type = c.type;
                _action.priceAmount = c.price;
                _action.active = true;

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
                Transform _languageTrasnform = __instance.transform.Find("window/language");
                Transform _windowTransform = __instance.transform.Find("window");

                if (_languageTrasnform == null || _windowTransform == null) return;

                // Create Buy Dropdown (Offset -70)
                EnsureDropdown(__instance, _windowTransform, _languageTrasnform,
                    "smarttrade_buy_setting", "Smart Trade Desired Buy Profit %",
                    "smarttrade.buyMargin");

                // Create Sell Dropdown (Offset -140)
                EnsureDropdown(__instance, _windowTransform, _languageTrasnform,
                    "smarttrade_sell_setting", "Smart Trade Desired Sell Profit %",
                    "smarttrade.sellMargin");
            }

            private static void EnsureDropdown(GeneralSettingsWindow __instance,
                Transform _parent,
                Transform _template,
                string _name,
                string _labelText,
                string _prefKey)
            {
                // Check if it already exists so we don't duplicate on every 'Show'
                Transform existing = _parent.Find(_name);
                if (existing != null) return;

                // 1. Clone the row
                GameObject row = UnityEngine.Object.Instantiate(_template.gameObject, _parent);
                row.name = _name;

                // 2. Setup the Label
                TMPro.TextMeshProUGUI label = row.transform.Find("label").GetComponent<TMPro.TextMeshProUGUI>();
                label.text = _labelText;

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
                int currentSaved = PlayerPrefs.GetInt(_prefKey, 10); // Default 10%
                int index = values.IndexOf(currentSaved);
                dropdown.SetValueWithoutNotify(index != -1 ? index : 2); // Default to index 2 (10%) if not found

                // 6. Save Logic
                dropdown.onValueChanged.AddListener((int val) =>
                {
                    int selectedValue = values[val];
                    PlayerPrefs.SetInt(_prefKey, selectedValue);
                    MelonLoader.MelonLogger.Msg($"{_labelText} updated to: {selectedValue}%");
                });
            }
        }

        // Helper methods must be STATIC and take parameters since they can't use 'this'
        public static void LogProfitableSales(GameState _gameState,
            PriceCalculator _priceCalc,
            List<TradeWindowGood> _goods,
            int _tradeAmount)
        {
            foreach (TradeWindowGood good in _goods)
            {
                // 1. Get the current city the player is looking at
                var city = _gameState.viewCity;

                // 2. Access the inventory from the selected ship or its convoy
                var ship = _gameState.selectedShip;
                if (ship == null || city == null) return;

                // Use the ship (which is a CargoHolder) to get the specific good
                var stack = ship.GetGood(good.GetGood());

                if (stack != null && stack.amount > 0)
                {
                    int cityBuyPriceTotal = _priceCalc.CityBuysGoods(good.GetGood(), city, _tradeAmount);
                    float currentUnitPrice = (float)cityBuyPriceTotal / _tradeAmount;

                    // Note: Ensure your 'stack' object contains an 'averageCost' field
                    if (currentUnitPrice > stack.averageCost)
                    {
                        Debug.Log($"Profit Alert: {good.GetGood()} is profitable at {city.name}.");
                    }
                }
            }
        }

        // --- MARKET ANALYSIS ---

        static float GetMarketPressure(City _city, string _good)
        {
            var productionDict = Production.GetCityProduction(_city);
            var consumptionDict = Consumption.GetCityConsumption(_city);

            float production = productionDict.ContainsKey(_good) ? productionDict[_good].amount : 0f;

            float consumption = consumptionDict.ContainsKey(_good)
                ? consumptionDict[_good].amount
                : 0f;

            float imbalance = consumption - production;
            float total = production + consumption + 1f;

            return imbalance / total;
        }

        // --- SCORING SYSTEM ---
        static float CalculateTradeScore(
            string _good,
            City _currentCity,
            List<City> _futureCities,
            GameState _gameState,
            PriceCalculator _priceCalc)
        {
            float bestScore = 0f;

            float sourcePressure = GetMarketPressure(_currentCity, _good);

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

                    float targetPressure = GetMarketPressure(futureCity, _good);

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