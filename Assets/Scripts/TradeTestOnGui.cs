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
            if (float.TryParse(amountStr, out var v)) tm.Buy(v);
        }

        if (GUI.Button(new Rect(160, 75, 140, 30), "Buy DOWN"))
        {
            tm.SetActiveOutcomeDown();
            if (float.TryParse(amountStr, out var v)) tm.Buy(v);
        }

        if (GUI.Button(new Rect(10, 115, 140, 30), "Sell (active)"))
        {
            tm.Sell();
        }

        GUI.Label(new Rect(10, 160, 600, 20), "State: " + tm.GetCurrentStates());
    }
}
