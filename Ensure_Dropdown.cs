using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using zip.lexy.tgame.ui.settings;
using Smart_Trade.Send_Melon_Logger_Message;

namespace Smart_Trade.Ensure_Dropdown
{
    internal class Ensure_Dropdown
    {
        public static void EnsureDropdown(GeneralSettingsWindow __instance,
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
                Smart_Trade.Send_Melon_Logger_Message.Send_Melon_Logger_Message.SendMelonLoggerMessage($"{_labelText} updated to: {selectedValue}%");
            });
        }
    }
}
