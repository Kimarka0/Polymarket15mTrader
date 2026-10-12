using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIDisplay : MonoBehaviour
{
    public static UIDisplay Instance;

    [Header("Market information")]
    [SerializeField] private TextMeshProUGUI marketTitleText;
    [SerializeField] private TextMeshProUGUI timeRemainingText;
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("BTC Prices")]
    [SerializeField] private TextMeshProUGUI currentPriceText;
    [SerializeField] private TextMeshProUGUI priceToBeatText;
    [SerializeField] private TextMeshProUGUI spreadText;

    [Header("Bucket Up")]
    [SerializeField] private TextMeshProUGUI bidUpValue;
    [SerializeField] private TextMeshProUGUI askUpValue;

    [Header("Bucket Down")]
    [SerializeField] private TextMeshProUGUI bidDownValue;
    [SerializeField] private TextMeshProUGUI askDownValue;

    [Header("Statistic")]
    [SerializeField] private TextMeshProUGUI volumeValue;

    [Header("Handle Buttons")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button stopButton;

    private MarketController marketController;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        ResetDisplay();
        SetupButtons();
    }

    private void SetupButtons()
    {
        marketController = FindAnyObjectByType<MarketController>();

        if (startButton != null)
        {
            startButton.onClick.AddListener(OnStartClicked);
        }

        if (stopButton != null)
        {
            stopButton.onClick.AddListener(OnStopClicked);
            stopButton.interactable = false;
        }
    }

    private void OnStartClicked()
    {
        if (marketController != null)
        {
            marketController.StartMonitoring();

            if (startButton != null) startButton.interactable = false;
            if (stopButton != null) stopButton.interactable = true;
        }
    }

    private void OnStopClicked()
    {
        if (marketController != null)
        {
            marketController.StopMonitoring();

            if (startButton != null) startButton.interactable = true;
            if (stopButton != null) stopButton.interactable = false;
        }
    }

    public void UpdateDisplay(ActiveMarketData data)
    {
        if (marketTitleText == null) return;

        try
        {

            marketTitleText.text = data.eventTitle;

            int secondsRemaining = (int)Math.Max(0, (data.sessionEndTime - DateTime.UtcNow).TotalSeconds);
            timeRemainingText.text = $"{secondsRemaining / 60:D2}:{secondsRemaining % 60:D2}";

            currentPriceText.text = $"${data.currentBtcPrice:F2}";
            priceToBeatText.text = $"${data.priceToBeatChainlink:F2}";

            double spread = data.spreadFromStart;
            spreadText.text = spread >= 0 ? $"+${spread:F2}" : $"-${Math.Abs(spread):F2}";

            bidUpValue.text = $"{data.bidUp:F3}";
            askUpValue.text = $"{data.askUp:F3}";

            bidDownValue.text = $"{data.bidDown:F3}";
            askDownValue.text = $"{data.askDown:F3}";

            volumeValue.text = $"${data.volumeThis15m:F2}";

            UpdateStatus(secondsRemaining);
        }
        catch (Exception e)
        {
            Debug.LogError($"UI Error: {e.Message}");
        }
    }

    private void UpdateStatus(int secondsRemaining)
    {
        if (statusText == null) return;

        if (secondsRemaining <= 0)
        {
            statusText.text = "ENDED";
        }
        else
        {
            statusText.text = "ACTIVE";
        }
    }

    public void SetErrorMessage(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    public void SetInfoMessage(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    public void SetResultMessage(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    public void ResetDisplay()
    {
        if (marketTitleText != null) marketTitleText.text = "WAITING FOR START";
        if (timeRemainingText != null) timeRemainingText.text = "--:--";
        if (statusText != null) statusText.text = "WAITING";

        if (currentPriceText != null) currentPriceText.text = "$--";
        if (priceToBeatText != null) priceToBeatText.text = "$--";
        if (spreadText != null) spreadText.text = "$--";

        if (bidUpValue != null) bidUpValue.text = "--";
        if (askUpValue != null) askUpValue.text = "--";
        if (bidDownValue != null) bidDownValue.text = "--";
        if (askDownValue != null) askDownValue.text = "--";

        if (volumeValue != null) volumeValue.text = "$--";
    }
}