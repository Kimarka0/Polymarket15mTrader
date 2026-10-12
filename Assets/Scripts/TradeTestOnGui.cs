using System.Globalization;
using UnityEngine;

public class TradeTestOnGui : MonoBehaviour
{
    [SerializeField] private PolymarketTradeManager tm;
    private string amountStr = "5";

    private void OnGUI()
    {
        if (tm == null)
        {
            GUI.Label(new Rect(10, 10, 500, 30), "Assign PolymarketTradeManager in inspector");
            return;
        }

        GUI.Label(new Rect(10, 10, 600, 20), "Trade тест (15m BTC market)");
        GUI.Label(new Rect(10, 40, 200, 20), "Buy amount (USD):");
        amountStr = GUI.TextField(new Rect(220, 40, 80, 22), amountStr);

        if (GUI.Button(new Rect(10, 75, 140, 30), "Buy UP"))
        {
            tm.SetActiveOutcomeUp();
            BuyWithEnteredAmount();
        }

        if (GUI.Button(new Rect(160, 75, 140, 30), "Buy DOWN"))
        {
            tm.SetActiveOutcomeDown();
            BuyWithEnteredAmount();
        }

        if (GUI.Button(new Rect(10, 115, 140, 30), "Sell (active)"))
        {
            tm.Sell();
        }

        GUI.Label(new Rect(10, 160, 600, 20), "State: " + tm.GetCurrentStates());
    }

    // InvariantCulture: "5.5" is parsed the same way whatever the OS locale is.
    private void BuyWithEnteredAmount()
    {
        if (float.TryParse(amountStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float usd))
            tm.Buy(usd);
        else
            Debug.LogWarning($"[TradeTest] Not a number: '{amountStr}'. Use a dot as the decimal separator, e.g. 5.5");
    }
}
