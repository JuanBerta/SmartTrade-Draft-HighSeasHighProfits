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
        public const string ENABLED_KEY = "smarttrade.enabled";
        public const string MARGIN_KEY = "smarttrade.loadMargin";

        public static float GetProfit() =>
            PlayerPrefs.GetFloat(PROFIT_KEY, 0.1f); // default 10%

        public static bool IsEnabled() => PlayerPrefs.GetInt(ENABLED_KEY, 0) == 0; // saved value is dropdown index: 0 = Enabled, 1 = Disabled

        public static void SetEnabled(bool enabled) => PlayerPrefs.SetInt(ENABLED_KEY, enabled ? 0 : 1);

        // Margin dropdown stores its index; options are 0%..50% in steps of 5, default index 2 = 10%
        public static float GetLoadMargin() => PlayerPrefs.GetInt(MARGIN_KEY, 2) * 5 / 100f;
    }

}
