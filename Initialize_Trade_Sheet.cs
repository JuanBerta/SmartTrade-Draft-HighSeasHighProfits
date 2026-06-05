using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using zip.lexy.tgame.constants;
using zip.lexy.tgame.state.ship.trading;

namespace Smart_Trade.Initialize_Trade_Sheet
{
    internal class Initialize_Trade_Sheet
    {
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
    }
}
