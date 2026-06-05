using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using zip.lexy.tgame.state.ship.trading;

namespace Smart_Trade.Disable_Actions
{
    internal class DisableActionsClass
    {
        // We disable all actions at the start of each recalculation to ensure a clean slate, allowing our logic to reactivate only the most optimal trades.
        public static void DisableActions(CityTradeSheet _currentSheet)
        {
            foreach (var _action in _currentSheet.actions) { _action.active = false; }
        }
    }
}
