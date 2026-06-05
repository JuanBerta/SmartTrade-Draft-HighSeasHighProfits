using HarmonyLib;
using lexyvents;
using Smart_Trade.Recalculate_Trade_Route;
using System.Collections.Generic;
using UnityEngine;
using zip.lexy.tgame.state.ship;

namespace Smart_Trade.Try_Recalculate
{
    [HarmonyPatch(typeof(EventBus), "Dispatch", new[] { typeof(object) })]
    public static class Try_Recalculate
    {
        static Dictionary<Ship, float> lastUpdate = new Dictionary<Ship, float>();

        public static void TryRecalculate(Ship ship)
        {
            float now = Time.time;
            if (lastUpdate.TryGetValue(ship, out float last))
            {
                // Throttling: Don't recalculate more than once per second per ship
                if (now - last < 1.0f) return;
            }

            lastUpdate[ship] = now;
            Smart_Trade.Recalculate_Trade_Route.Recalculate_Trade_Route.RecalculateTradeRoute(ship);
        }
    }
}