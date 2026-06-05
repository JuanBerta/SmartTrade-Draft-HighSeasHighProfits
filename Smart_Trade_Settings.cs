using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Smart_Trade.Smart_Trade_Settings
{
    public static class SmartTradeSettings
    {
        public const string PROFIT_KEY = "smarttrade.profitMargin";

        public static float GetProfit() =>
            PlayerPrefs.GetFloat(PROFIT_KEY, 0.1f); // default 10%
    }

}
