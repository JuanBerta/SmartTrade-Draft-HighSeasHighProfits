# SmartTrade-Draft-HighSeasHighProfits
SmartTrade Draft is a MelonLoader mod for the economic simulation game High Seas, High Profits. It overhauls the default automated trading behavior to make your convoys think like actual merchants rather than simple transporters.

## 🚀 Features
Dynamic Market Scoring: Evaluates trades based on a combination of immediate profit, market pressure (supply vs. demand), and inventory levels.

Production Awareness: Prioritizes buying goods where they are produced in surplus and selling them in "black hole" cities where local production is zero.

In-Game UI Settings: Adds custom dropdowns to the General Settings window to set your desired Buy and Sell profit margins (0% - 50%).

Route Prediction: Convoys analyze the entire trade route to find the most profitable destination for a good, not just the next stop.

Smart Throttling: Efficiently recalculates routes only when cargo changes, saving CPU cycles.

## 🛠 Installation
1- Ensure MelonLoader is installed for High Seas, High Profits.

2- Download the Smart Trade.dll.

3- Place the file into your game's Mods folder.

4- Launch the game.

## ⚙️ Configuration
You can find the mod settings by going to:
Settings > General Settings
Look for:

Smart Trade Desired Buy Profit %: The minimum margin required before a convoy will buy a good.

Smart Trade Desired Sell Profit %: The minimum margin required before a convoy will sell its current cargo.

## Instructions in game:
1- Select "Smart Trade Desired Buy Profit %" (recommended 10%)

2- Select "Smart Trade Desired Sell Profit %" (recommended to 0% if using a buy profit of 10%)

3- In the auto trade route menu, in "World Map", select each city that you want to trade (Buying/Selling quick action will be made automatically)

4- Activate auto trade route as a vanilla one

## 🏗 Developer Notes & Forking
This mod is a draft implementation using Harmony for runtime patching.

Key Systems:
CalculateTradeScore: The heart of the mod. Uses market imbalance to weight potential trades.

SmartTrade_CargoChangedPatch: Triggers route recalculations whenever a ship's inventory is updated.

GetMarketPressure: Logic that calculates if a city is "starving" for a specific good based on consumption vs. production.

## ⚖️ License
This project is licensed under the MIT License.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY.
