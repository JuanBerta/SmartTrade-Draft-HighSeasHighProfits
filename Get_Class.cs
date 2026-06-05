using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using zip.lexy.tgame.simulation.consumption;
using zip.lexy.tgame.state;
using zip.lexy.tgame.state.building;
using zip.lexy.tgame.state.city;
using zip.lexy.tgame.state.ship;
using zip.lexy.tgame.state.ship.trading;
using zip.lexy.tgame.state.trader;
using zip.lexy.tgame.ui.gamegeneration;

namespace Smart_Trade.Get_Class
{
    internal class Get_Class
    {
        public static Ship GetShip(GameState _gameState)
        {
            return _gameState?.selectedShip;
        }

        public static Human GetHuman(GameState _gameState)
        {
            return _gameState.human;
        }

        // We access the GameState directly from the InstanceProvider, which is more efficient than trying to find it through the player's fleet or trade routes,
        public static GameState GetGameState()
        {
            return InstanceProvider.GetInstance<GameState>();
        }

        // We access the PriceCalculator directly from the InstanceProvider,
        // which is more efficient than trying to find it through the player's fleet or trade routes,
        public static PriceCalculator GetPriceCalculator()
        {
            return InstanceProvider.GetInstance<PriceCalculator>();
        }

        // We access the Convoy directly from the ship, which is more efficient than searching through the player's fleet,
        public static Convoy GetConvoy(Ship _ship)
        {
            return _ship?.convoy;
        }

        // We get the TradeRoute directly from the ship's convoy, which is more efficient than searching through the player's trade routes,
        // especially since we're already operating on a ship that has an active route.
        public static TradeRoute GetTradeRoute(Ship _ship)
        {
            return _ship?.convoy?.tradeRoute;
        }

        public static List<City> GetCurrentSheet(TradeRoute _tradeRoute, GameState _gameState)
        {
            return new List<CityTradeSheet>(_tradeRoute.tradeSheets).Select(s => _gameState.cities[s.cityId])
                .ToList();
        }

        // We use a tuple list to store candidate trades with all necessary info for decision-making and application.
        public static List<(string, int, float, int, int)> GetCandidate()
        {
            return new List<(string good, int type, float score, int price, int tradeDepth)>();
        }

        // Since Convoy implements CargoHolder, we can directly cast it to access the cargo without needing to find the ship or player.
        public static CargoHolder GetCargoHolder(Convoy _convoy)
        {
            CargoHolder _cargoSource = (CargoHolder)_convoy;
            return _cargoSource;
        }

        // --- MARKET ANALYSIS ---
        public static float GetMarketPressure(City _city, string _good)
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
    }
}
