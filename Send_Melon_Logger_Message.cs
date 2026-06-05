using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Smart_Trade.Send_Melon_Logger_Message
{
    internal class Send_Melon_Logger_Message
    {
        // Send a MelonLogger Message
        public static void SendMelonLoggerMessage(string message)
        {
            MelonLogger.Msg($"{message}");
        }
    }
}
