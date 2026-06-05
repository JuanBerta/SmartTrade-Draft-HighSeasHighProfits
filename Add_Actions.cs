using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using zip.lexy.tgame.constants;
using zip.lexy.tgame.state.ship.trading;

namespace Smart_Trade.Add_Actions
{
    internal class Add_Actions
    {
        // We ensure that every TradeSheet has an action entry for every good, even if it's inactive,
        // to maintain a consistent interface and allow the ship to reactivate trades as it acquires new goods.
        public static void AddActions(CityTradeSheet _currentSheet)
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
    }
}
