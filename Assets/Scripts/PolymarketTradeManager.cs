using System;
using System.Collections;
using System.Numerics;
using UnityEngine;
using Nethereum.Signer;
using Nethereum.Signer.EIP712;

public class PolymarketTradeManager : MonoBehaviour, TradeManager
{
    [Header("Refs")]
    [SerializeField] private MarketController marketController;
    [SerializeField] private PolymarketL2CredsBootstrap credsBootstrap;

    [Header("Trading")]
    [SerializeField] private string clobHost = PolymarketConstants.CLOB_HOST;
    [SerializeField] private int chainId = PolymarketConstants.CHAIN_ID_POLYGON;

    [Tooltip("Off: orders are built and signed, but POST /order is not sent (dry run).")]
    [SerializeField] private bool realTradingEnabled = false;

    [Tooltip("0=EOA, 1=POLY_PROXY, 2=GNOSIS_SAFE (смотри доки).")]
    [SerializeField] private int signatureType = 0;

    [Tooltip("Если signatureType != 0, тут должен быть funder (proxy address из polymarket.com/settings).")]
    [SerializeField] private string funderAddress = "";

    [Header("Sell retry")]
    [SerializeField] private float sellRetryDelaySeconds = 0.75f;
    [SerializeField] private int sellMaxAttempts = 120;

    [Header("Tick")]
    [SerializeField] private float tickEmitMinInterval = 0.25f;

    public event Action MarketStartedEvent;
    public event Action MarketFinishedEvent;
    public event Action SellComplitedEvent;
    public event Action BuyComplitedEvent;
    public event Action<MarketManagerStates> StateChangesEvent;
    public event Action<MarketDataTik> MarketDataTickEvent;

    private MarketManagerStates state = MarketManagerStates.WaitInitedManager;
    private string lastError = "";

    private PolymarketClobRestClient clob;
    private readonly PolymarketDataApiClient dataApi = new PolymarketDataApiClient();
    private float lastTickEmitTime;

    // Upper limit for one Buy, in USD.
    private const float MaxOrderUsd = 100f;

    // Minimum order size in shares (per the author).
    private const decimal MinSellShares = 5m;

    // Set while a Buy or Sell coroutine runs, so a second click cannot send a second order.
    private bool isOrderInFlight;

    private enum Outcome { Up, Down }
    private Outcome activeOutcome = Outcome.Up;

    // Last seen market, to detect a market switch in Update.
    private string currentUpToken;
    private string currentDownToken;
    private string currentConditionId;
    private DateTime currentMarketEndUtc;

    private void Awake()
    {
        clob = new PolymarketClobRestClient(clobHost);
        SetState(MarketManagerStates.WaitStartMarket);
    }

    private void Update()
    {
        if (marketController == null) return;
        var ms = marketController.GetMarketStateSafe();
        if (ms == null || !ms.IsInitialized) return;

        bool changed = false;

        if (currentUpToken != ms.upTokenId) { currentUpToken = ms.upTokenId; changed = true; }
        if (currentDownToken != ms.downTokenId) { currentDownToken = ms.downTokenId; changed = true; }
        if (currentConditionId != ms.conditionId) { currentConditionId = ms.conditionId; changed = true; }
        if (currentMarketEndUtc != ms.Current.sessionEndTime) { currentMarketEndUtc = ms.Current.sessionEndTime; }

        if (changed && !string.IsNullOrEmpty(currentUpToken) && !string.IsNullOrEmpty(currentDownToken))
        {
            MarketStartedEvent?.Invoke();
            SetState(MarketManagerStates.InProgress);
        }

        if (Time.unscaledTime - lastTickEmitTime >= tickEmitMinInterval)
        {
            lastTickEmitTime = Time.unscaledTime;
            EmitTick(ms);
        }

        if (state == MarketManagerStates.InProgress)
        {
            float tte = (float)(currentMarketEndUtc - DateTime.UtcNow).TotalSeconds;
            if (tte <= 0f)
            {
                SetState(MarketManagerStates.MarketFinished);
                MarketFinishedEvent?.Invoke();
            }
        }
    }

    public void SetActiveOutcomeUp() => activeOutcome = Outcome.Up;
    public void SetActiveOutcomeDown() => activeOutcome = Outcome.Down;

    public MarketManagerStates GetCurrentStates() => state;

    private void SetState(MarketManagerStates s)
    {
        if (state == s) return;
        state = s;
        StateChangesEvent?.Invoke(state);
    }

    private void EmitTick(MarketState ms)
    {
        // Best bid/ask are already computed by MarketState.UpdateFromRealOrderBook().
        var d = ms.Current;

        float tte = (float)(d.sessionEndTime - DateTime.UtcNow).TotalSeconds;
        if (tte < 0) tte = 0;

        var tick = new MarketDataTik
        {
            timeToEnd = tte,
            askUp = (float)d.askUp,
            askDown = (float)d.askDown,
            bidUp = (float)d.bidUp,
            bidDown = (float)d.bidDown,
            spread = (float)d.spreadFromStart,
            volume = (float)d.volumeThis15m
        };

        MarketDataTickEvent?.Invoke(tick);
    }

    public void Buy(float value)
    {
        if (state != MarketManagerStates.InProgress)
        {
            Debug.LogWarning("[TradeManager] Buy ignored: " + NotReadyReason());
            return;
        }

        if (!IsValidOrderAmount(value))
        {
            Debug.LogWarning($"[TradeManager] Buy ignored: amount must be a number greater than 0 and at most {MaxOrderUsd} USD.");
            return;
        }

        if (isOrderInFlight)
        {
            Debug.LogWarning("[TradeManager] Buy ignored: another order is still in progress.");
            return;
        }

        isOrderInFlight = true;
        StartCoroutine(RunOrder(BuyCoroutine(value)));
    }

    public void Sell()
    {
        if (state != MarketManagerStates.InProgress)
        {
            Debug.LogWarning("[TradeManager] Sell ignored: " + NotReadyReason());
            return;
        }

        if (isOrderInFlight)
        {
            Debug.LogWarning("[TradeManager] Sell ignored: another order is still in progress.");
            return;
        }

        isOrderInFlight = true;
        StartCoroutine(RunOrder(SellCoroutine()));
    }

    private static bool IsValidOrderAmount(float usd)
    {
        return !float.IsNaN(usd) && !float.IsInfinity(usd) && usd > 0f && usd <= MaxOrderUsd;
    }

    // Runs an order coroutine and clears isOrderInFlight on every exit:
    // normal end, yield break or an exception thrown inside the order.
    private IEnumerator RunOrder(IEnumerator order)
    {
        try
        {
            while (order.MoveNext())
                yield return order.Current;
        }
        finally
        {
            isOrderInFlight = false;
        }
    }

    // Unity does not run finally blocks of coroutines that are stopped or disabled.
    private void OnDisable()
    {
        isOrderInFlight = false;
    }

    private IEnumerator BuyCoroutine(float usdToSpend)
    {
        var ms = marketController.GetMarketStateSafe();
        if (ms == null || !ms.IsInitialized)
        {
            OrderFailed("Buy not sent: market data is not loaded yet. Try again in a few seconds.");
            yield break;
        }

        string tokenId = (activeOutcome == Outcome.Up) ? ms.upTokenId : ms.downTokenId;
        if (string.IsNullOrEmpty(tokenId))
        {
            OrderFailed("Buy not sent: the token of the new market is not loaded yet. Try again in a few seconds.");
            yield break;
        }

        double ask = (activeOutcome == Outcome.Up) ? ms.Current.askUp : ms.Current.askDown;
        if (!OrderAmountCalculator.TryCalculateBuy(usdToSpend, ask, out OrderAmounts amounts, out string amountError))
        {
            OrderFailed("Buy not sent: " + amountError);
            yield break;
        }

        // feeRateBps is a required field of POST /order.
        string feeRateBps = "0";
        bool feeOk = false;
        yield return StartCoroutine(clob.GetFeeRateBps(
            tokenId,
            ok => { feeRateBps = ok; feeOk = true; },
            err => { Debug.LogWarning(err); feeOk = true; feeRateBps = "0"; }
        ));
        while (!feeOk) yield return null;

        if (!credsBootstrap.TryGetApiCreds(out string apiKey, out string secret, out string pass))
        {
            OrderFailed("Buy not sent: API credentials are not ready (bootstrap is still running or failed, see the log above).");
            yield break;
        }

        // Must be the same EOA that created the API credentials.
        string signerAddress = credsBootstrap.GetSignerAddress();
        string maker = (signatureType == 0) ? signerAddress : funderAddress;
        if (string.IsNullOrEmpty(maker))
        {
            Fail("Buy: maker/funder empty (signatureType !=0 but no funderAddress)");
            yield break;
        }

        long serverTs = 0;
        yield return StartCoroutine(credsBootstrap.GetServerTime(ts => serverTs = ts));
        if (serverTs <= 0) { OrderFailed("Buy not sent: could not get the server time. Try again."); yield break; }
        string tsString = serverTs.ToString();

        // Amounts are integers in 1e6 units.
        BigInteger makerAmount = new BigInteger(amounts.MakerAmount); // USDC micro
        BigInteger takerAmount = new BigInteger(amounts.TakerAmount); // shares micro

        // BUY order: maker pays USDC, taker receives shares.

        BigInteger expiration = new BigInteger(serverTs + 60); // expires in 60 s
        BigInteger nonce = BigInteger.Zero; // 0 unless orders are cancelled by nonce
        BigInteger salt = new BigInteger((ulong)UnityEngine.Random.Range(1, int.MaxValue));

        string typedJson = BuildOrderTypedJson(
            chainId, maker, signerAddress, PolymarketConstants.ZERO_ADDRESS,
            tokenId, makerAmount, takerAmount, expiration, nonce, feeRateBps, side: 0, salt: salt
        );

        var ecKey = new EthECKey(credsBootstrap.GetPrivateKeyHex()); // key from Secrets/polymarket.local.json
        string orderSig = new Eip712TypedDataSigner().SignTypedDataV4(typedJson, ecKey);

        string payload = BuildPostOrderPayload(
            salt, maker, signerAddress, PolymarketConstants.ZERO_ADDRESS,
            tokenId, makerAmount, takerAmount, expiration, nonce, feeRateBps, side: 0,
            signatureType, orderSig,
            ownerApiKey: apiKey,
            orderType: "FOK",
            postOnly: false
        );

        if (!realTradingEnabled)
        {
            LogDryRun("BUY", tokenId, ask, makerAmount, takerAmount, expiration);
            yield break;
        }

        yield return StartCoroutine(clob.PostOrder(
            apiKey: apiKey,
            secret: secret,
            passphrase: pass,
            polyAddress: signerAddress,
            timestamp: tsString,
            ownerApiKey: apiKey,
            orderJson: payload,
            onDone: (result, resp, details) =>
            {
                if (result == OrderSendResult.Accepted)
                {
                    Debug.Log($"[BUY] success status={resp.status} orderId={resp.orderId}");
                    BuyComplitedEvent?.Invoke();
                }
                else if (result == OrderSendResult.Rejected)
                {
                    OrderFailed($"Buy rejected by the exchange ({details}). Nothing was bought, you can try again.");
                }
                else
                {
                    OrderFailed($"Buy: no clear answer from POST /order ({details}). The result is unknown: check your position before trying again.");
                }
            }
        ));
    }

    private IEnumerator SellCoroutine()
    {
        var ms = marketController.GetMarketStateSafe();
        if (ms == null || !ms.IsInitialized)
        {
            OrderFailed("Sell not sent: market data is not loaded yet. Try again in a few seconds.");
            yield break;
        }

        string tokenId = (activeOutcome == Outcome.Up) ? ms.upTokenId : ms.downTokenId;
        if (string.IsNullOrEmpty(tokenId))
        {
            OrderFailed("Sell not sent: the token of the new market is not loaded yet. Try again in a few seconds.");
            yield break;
        }

        if (!credsBootstrap.TryGetApiCreds(out string apiKey, out string secret, out string pass))
        {
            OrderFailed("Sell not sent: API credentials are not ready (bootstrap is still running or failed, see the log above).");
            yield break;
        }

        string signerAddress = credsBootstrap.GetSignerAddress();
        string maker = (signatureType == 0) ? signerAddress : funderAddress;
        if (string.IsNullOrEmpty(maker))
        {
            Fail("Sell: maker/funder empty (signatureType !=0 but no funderAddress)");
            yield break;
        }

        string feeRateBps = "0";
        bool feeOk = false;
        yield return StartCoroutine(clob.GetFeeRateBps(
            tokenId,
            ok => { feeRateBps = ok; feeOk = true; },
            err => { Debug.LogWarning(err); feeOk = true; feeRateBps = "0"; }
        ));
        while (!feeOk) yield return null;

        // Requirement: retry until the sell succeeds (up to sellMaxAttempts).
        for (int attempt = 1; attempt <= sellMaxAttempts; attempt++)
        {
            // Ask for the position before every attempt: an earlier attempt may have filled
            // even if its response was lost, so never reuse an old size.
            decimal positionSize = 0m;
            string positionError = null;
            yield return StartCoroutine(dataApi.GetPositionSize(
                maker, tokenId,
                size => positionSize = size,
                err => positionError = err
            ));

            if (positionError != null)
            {
                Debug.LogError($"[SELL] Could not load the position from Data API ({positionError}). Nothing was sold.");
                yield break;
            }

            decimal sharesToSell = OrderAmountCalculator.RoundDownShares(positionSize);
            if (sharesToSell <= 0m)
            {
                if (attempt == 1)
                    Debug.LogError("[SELL] No position in this outcome according to Data API. " +
                                   "Data API updates with a delay after a buy: try again in a few seconds.");
                else
                    Debug.LogWarning($"[SELL] The position is gone before attempt {attempt}, probably sold by an earlier attempt. Stopping.");
                yield break;
            }

            if (sharesToSell < MinSellShares)
            {
                Debug.LogError($"[SELL] Position is {sharesToSell} shares, below the minimum order size of {MinSellShares}. Nothing was sold.");
                yield break;
            }

            // Price at the best bid so the FOK order can fill immediately.
            double bid = (activeOutcome == Outcome.Up) ? ms.Current.bidUp : ms.Current.bidDown;
            if (bid <= 0.000001)
            {
                Debug.LogWarning("[SELL] bid invalid; retry...");
                yield return new WaitForSeconds(sellRetryDelaySeconds);
                continue;
            }

            long serverTs = 0;
            yield return StartCoroutine(credsBootstrap.GetServerTime(ts => serverTs = ts));
            if (serverTs <= 0) { OrderFailed("Sell not sent: could not get the server time. Try again."); yield break; }
            string tsString = serverTs.ToString();

            if (!OrderAmountCalculator.TryCalculateSell(sharesToSell, bid, out OrderAmounts amounts, out string amountError))
            {
                OrderFailed("Sell not sent: " + amountError);
                yield break;
            }

            BigInteger makerAmount = new BigInteger(amounts.MakerAmount); // shares
            BigInteger takerAmount = new BigInteger(amounts.TakerAmount); // USDC

            BigInteger expiration = new BigInteger(serverTs + 60);
            BigInteger nonce = BigInteger.Zero;
            BigInteger salt = new BigInteger((ulong)UnityEngine.Random.Range(1, int.MaxValue));

            string typedJson = BuildOrderTypedJson(
                chainId, maker, signerAddress, PolymarketConstants.ZERO_ADDRESS,
                tokenId, makerAmount, takerAmount, expiration, nonce, feeRateBps, side: 1, salt: salt
            );

            var ecKey = new EthECKey(credsBootstrap.GetPrivateKeyHex());
            string orderSig = new Eip712TypedDataSigner().SignTypedDataV4(typedJson, ecKey);

            string payload = BuildPostOrderPayload(
                salt, maker, signerAddress, PolymarketConstants.ZERO_ADDRESS,
                tokenId, makerAmount, takerAmount, expiration, nonce, feeRateBps, side: 1,
                signatureType, orderSig,
                ownerApiKey: apiKey,
                orderType: "FOK",
                postOnly: false
            );

            if (!realTradingEnabled)
            {
                LogDryRun("SELL", tokenId, bid, makerAmount, takerAmount, expiration);
                yield break;
            }

            OrderSendResult sendResult = OrderSendResult.Unknown;

            yield return StartCoroutine(clob.PostOrder(
                apiKey: apiKey,
                secret: secret,
                passphrase: pass,
                polyAddress: signerAddress,
                timestamp: tsString,
                ownerApiKey: apiKey,
                orderJson: payload,
                onDone: (result, resp, details) =>
                {
                    sendResult = result;
                    if (result == OrderSendResult.Accepted)
                        Debug.Log($"[SELL] attempt={attempt} success status={resp.status} orderId={resp.orderId}");
                    else if (result == OrderSendResult.Rejected)
                        Debug.LogWarning($"[SELL] attempt={attempt} rejected by the exchange ({details}).");
                    else
                        Debug.LogWarning($"[SELL] attempt={attempt}: no clear answer from POST /order ({details}). " +
                                         "The result is unknown; the position is checked again before the next attempt.");
                }
            ));

            if (sendResult == OrderSendResult.Accepted)
            {
                SellComplitedEvent?.Invoke();
                yield break;
            }

            yield return new WaitForSeconds(sellRetryDelaySeconds);
        }

        OrderFailed($"Sell: not filled after {sellMaxAttempts} attempts. Check your position before trying again.");
    }

    // Signed order summary for dry runs. No signature, keys or API credentials.
    private void LogDryRun(string side, string tokenId, double price, BigInteger makerAmount, BigInteger takerAmount, BigInteger expiration)
    {
        string outcome = (activeOutcome == Outcome.Up) ? "UP" : "DOWN";
        string tokenTail = tokenId.Length > 6 ? tokenId.Substring(tokenId.Length - 6) : tokenId;
        Debug.Log($"[DRY RUN] {side} {outcome}: price={price} makerAmount={makerAmount} takerAmount={takerAmount} " +
                  $"expiration={expiration} token=...{tokenTail}. Order signed, not sent (realTradingEnabled is off).");
    }

    // An order did not go through. Trading stays available, so the user can try again.
    private void OrderFailed(string message)
    {
        lastError = message;
        Debug.LogError("[TradeManager] " + message);
    }

    // A setup problem that a retry cannot fix (for example no funder address). Stops trading until the next market.
    private void Fail(string err)
    {
        lastError = err;
        Debug.LogError("[TradeManager] " + err);
        SetState(MarketManagerStates.Error);
    }

    private string NotReadyReason()
    {
        if (state == MarketManagerStates.Error)
            return $"trading is stopped by an error: {lastError}";
        return $"the market is not ready for trading yet (state={state}). Wait until the new market has loaded.";
    }

    private string BuildOrderTypedJson(
        int chainId,
        string maker,
        string signer,
        string taker,
        string tokenIdStr,
        BigInteger makerAmount,
        BigInteger takerAmount,
        BigInteger expiration,
        BigInteger nonce,
        string feeRateBpsStr,
        int side,
        BigInteger salt
    )
    {
        BigInteger fee = BigInteger.Zero;
        BigInteger.TryParse(feeRateBpsStr, out fee);

        BigInteger tokenId = BigInteger.Zero;
        BigInteger.TryParse(tokenIdStr, out tokenId);

        // EIP-712 typed data JSON for the order, built by hand. Field order must match the contract.
        string json =
            "{"
            + "\"types\":{"
            + "\"EIP712Domain\":["
            + "{\"name\":\"name\",\"type\":\"string\"},"
            + "{\"name\":\"version\",\"type\":\"string\"},"
            + "{\"name\":\"chainId\",\"type\":\"uint256\"},"
            + "{\"name\":\"verifyingContract\",\"type\":\"address\"}"
            + "],"
            + "\"Order\":["
            + "{\"name\":\"salt\",\"type\":\"uint256\"},"
            + "{\"name\":\"maker\",\"type\":\"address\"},"
            + "{\"name\":\"signer\",\"type\":\"address\"},"
            + "{\"name\":\"taker\",\"type\":\"address\"},"
            + "{\"name\":\"tokenId\",\"type\":\"uint256\"},"
            + "{\"name\":\"makerAmount\",\"type\":\"uint256\"},"
            + "{\"name\":\"takerAmount\",\"type\":\"uint256\"},"
            + "{\"name\":\"expiration\",\"type\":\"uint256\"},"
            + "{\"name\":\"nonce\",\"type\":\"uint256\"},"
            + "{\"name\":\"feeRateBps\",\"type\":\"uint256\"},"
            + "{\"name\":\"side\",\"type\":\"uint8\"}"
            + "]"
            + "},"
            + "\"primaryType\":\"Order\","
            + "\"domain\":{"
            + $"\"name\":\"{PolymarketConstants.ORDER_DOMAIN_NAME}\","
            + $"\"version\":\"{PolymarketConstants.ORDER_DOMAIN_VERSION}\","
            + $"\"chainId\":\"{chainId}\","
            + $"\"verifyingContract\":\"{PolymarketConstants.EXCHANGE_VERIFYING_CONTRACT}\""
            + "},"
            + "\"message\":{"
            + $"\"salt\":\"{salt}\","
            + $"\"maker\":\"{maker}\","
            + $"\"signer\":\"{signer}\","
            + $"\"taker\":\"{taker}\","
            + $"\"tokenId\":\"{tokenId}\","
            + $"\"makerAmount\":\"{makerAmount}\","
            + $"\"takerAmount\":\"{takerAmount}\","
            + $"\"expiration\":\"{expiration}\","
            + $"\"nonce\":\"{nonce}\","
            + $"\"feeRateBps\":\"{fee}\","
            + $"\"side\":\"{side}\""
            + "}"
            + "}";

        return json;
    }

    private string BuildPostOrderPayload(
        BigInteger salt,
        string maker,
        string signer,
        string taker,
        string tokenIdStr,
        BigInteger makerAmount,
        BigInteger takerAmount,
        BigInteger expiration,
        BigInteger nonce,
        string feeRateBpsStr,
        int side,
        int signatureType,
        string signatureHex,
        string ownerApiKey,
        string orderType,
        bool postOnly
    )
    {
        return
            "{"
            + "\"order\":{"
            + $"\"salt\":\"{salt}\","
            + $"\"maker\":\"{maker}\","
            + $"\"signer\":\"{signer}\","
            + $"\"taker\":\"{taker}\","
            + $"\"tokenId\":\"{tokenIdStr}\","
            + $"\"makerAmount\":\"{makerAmount}\","
            + $"\"takerAmount\":\"{takerAmount}\","
            + $"\"expiration\":\"{expiration}\","
            + $"\"nonce\":\"{nonce}\","
            + $"\"feeRateBps\":\"{feeRateBpsStr}\","
            + $"\"side\":\"{side}\","
            + $"\"signatureType\":{signatureType},"
            + $"\"signature\":\"{signatureHex}\""
            + "},"
            + $"\"owner\":\"{ownerApiKey}\","
            + $"\"orderType\":\"{orderType}\","
            + $"\"postOnly\":{(postOnly ? "true" : "false")}"
            + "}";
    }
}
