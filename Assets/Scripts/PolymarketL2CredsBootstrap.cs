using System;
using System.Collections;
using System.Numerics;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

using Nethereum.ABI.EIP712;
using Nethereum.Signer;
using Nethereum.Signer.EIP712;  
using System.Collections.Generic;

public class PolymarketL2CredsBootstrap : MonoBehaviour
{
    [Header("CLOB")]
    [SerializeField] private string clobHost = "https://clob.polymarket.com";
    [SerializeField] private int chainId = 137; // Polygon mainnet

    [Header("Signer (L1)")]
    [Tooltip("Private key (0x...) ТОЛЬКО для теста. Не храни так в проде.")]
    [SerializeField] private string privateKeyHex = "0xYOUR_PRIVATE_KEY";

    [Tooltip("Nonce для API-key. Если использовал раньше и потерял creds — derive работает только с тем же nonce.")]
    [SerializeField] private string nonceString = "0"; // string: a uint256 nonce does not fit into int

    [Header("Debug output")]
    [SerializeField] private bool logAll = true;

    // Fixed text required by Polymarket for L1 auth.
    private const string L1_MESSAGE = "This message attests that I control the given wallet";
    private string signerAddressCached = "";

    // Field names must match the /auth/* JSON for JsonUtility.
    [Serializable]
    public class ApiCredsResponse
    {
        public string apiKey;
        public string secret;
        public string passphrase;
    }

    private void Start()
    {
        StartCoroutine(GetOrCreateApiCredsFlow());
    }

    private IEnumerator GetOrCreateApiCredsFlow()
    {
        // Accept the key with or without the 0x prefix.
        if (!privateKeyHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            privateKeyHex = "0x" + privateKeyHex;

        if (!BigInteger.TryParse(nonceString, out var nonce))
        {
            Debug.LogError("Nonce parse error. Укажи nonce как целое число (например 0).");
            yield break;
        }

        EthECKey ecKey;
        try
        {
            ecKey = new EthECKey(privateKeyHex);
        }
        catch (Exception)
        {
            Debug.LogError("Invalid private key.");
            yield break;
        }

        // The POLY_ADDRESS header must be the signer address.
        string signerAddress = ecKey.GetPublicAddress();
        signerAddressCached = signerAddress;

        if (logAll)
            Debug.Log($"Signer address = {signerAddress}");

        // Use server time, not the local clock: the API checks the timestamp.
        long serverTs = 0;
        yield return StartCoroutine(GetServerTime((ts) => serverTs = ts));

        if (serverTs <= 0)
        {
            Debug.LogError("Не удалось получить server time. Проверь доступ к clobHost.");
            yield break;
        }

        if (logAll)
            Debug.Log($"CLOB server time (unix sec) = {serverTs}");

        // In ClobAuth the timestamp is a string, not a uint256.
        string tsString = serverTs.ToString();

        string l1Sig = PolymarketClobAuthSigner.SignClobAuth(
    chainId: chainId,
    key: ecKey,
    signerAddress: signerAddress,
    timestampString: tsString,
    nonce: nonce,
    message: L1_MESSAGE
);

        // Try derive first: it returns existing credentials and creates nothing.
        yield return StartCoroutine(CallDeriveApiKey(
            signerAddress: signerAddress,
            l1Signature: l1Sig,
            timestampUnixString: tsString,
            nonceString: nonce.ToString(),
            onSuccess: (creds) =>
            {
                Debug.Log("Derived existing API credentials.");
                SaveCredsToPlayerPrefs(creds);
                StartCoroutine(TestBalanceAllowance()); // TEST ONLY
            },
            onNotFoundOrError: () =>
            {
                // No existing credentials: create new ones.
                StartCoroutine(CallCreateApiKey(
                    signerAddress: signerAddress,
                    l1Signature: l1Sig,
                    timestampUnixString: tsString,
                    nonceString: nonce.ToString(),
                    onSuccess: (creds) =>
                    {
                        Debug.Log("Created new API credentials.");
                        SaveCredsToPlayerPrefs(creds);
                        Debug.Log("Starting L2 test...");
                        StartCoroutine(TestBalanceAllowance()); // TEST ONLY
                    }
                ));
            }
        ));
    }

    public IEnumerator GetServerTime(Action<long> onDone)
    {
        string url = $"{clobHost}/time";

        using (var req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"GetServerTime error: {req.error} | {req.downloadHandler?.text}");
                onDone(0);
                yield break;
            }

            // The body is a number or JSON with a number: try a plain parse, then keep only the digits.
            string body = req.downloadHandler.text?.Trim() ?? "";

            if (long.TryParse(body, out var tsDirect))
            {
                onDone(tsDirect);
                yield break;
            }

            var digits = new StringBuilder();
            foreach (char c in body)
                if (char.IsDigit(c))
                    digits.Append(c);

            if (long.TryParse(digits.ToString(), out var tsFallback))
            {
                onDone(tsFallback);
                yield break;
            }

            Debug.LogError("GetServerTime parse error. Body=" + body);
            onDone(0);
        }
    }

    private IEnumerator CallDeriveApiKey(
        string signerAddress,
        string l1Signature,
        string timestampUnixString,
        string nonceString,
        Action<ApiCredsResponse> onSuccess,
        Action onNotFoundOrError)
    {
        string url = $"{clobHost}/auth/derive-api-key";

        using (var req = UnityWebRequest.Get(url))
        {
            req.SetRequestHeader("POLY_ADDRESS", signerAddress);
            req.SetRequestHeader("POLY_SIGNATURE", l1Signature);
            req.SetRequestHeader("POLY_TIMESTAMP", timestampUnixString);
            req.SetRequestHeader("POLY_NONCE", nonceString);

            yield return req.SendWebRequest();

            // Often 404/4xx when there are no credentials to derive.
            if (req.result != UnityWebRequest.Result.Success)
            {
                if (logAll)
                    Debug.LogWarning($"DeriveApiKey failed: {req.responseCode} {req.error}");
                onNotFoundOrError?.Invoke();
                yield break;
            }

            string json = req.downloadHandler.text;
            var creds = JsonUtility.FromJson<ApiCredsResponse>(json);

            if (string.IsNullOrWhiteSpace(creds?.apiKey))
            {
                Debug.LogWarning("DeriveApiKey: response has no apiKey.");
                onNotFoundOrError?.Invoke();
                yield break;
            }

            onSuccess?.Invoke(creds);
        }
    }

    private IEnumerator CallCreateApiKey(
        string signerAddress,
        string l1Signature,
        string timestampUnixString,
        string nonceString,
        Action<ApiCredsResponse> onSuccess)
    {
        string url = $"{clobHost}/auth/api-key";

        // The API needs no body; an empty JSON object keeps the UnityWebRequest POST simple.
        byte[] bodyRaw = Encoding.UTF8.GetBytes("{}");

        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            req.SetRequestHeader("POLY_ADDRESS", signerAddress);
            req.SetRequestHeader("POLY_SIGNATURE", l1Signature);
            req.SetRequestHeader("POLY_TIMESTAMP", timestampUnixString);
            req.SetRequestHeader("POLY_NONCE", nonceString);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"CreateApiKey failed: {req.responseCode} {req.error}");
                yield break;
            }

            string json = req.downloadHandler.text;
            var creds = JsonUtility.FromJson<ApiCredsResponse>(json);

            if (string.IsNullOrWhiteSpace(creds?.apiKey))
            {
                Debug.LogError("CreateApiKey: response has no apiKey.");
                yield break;
            }

            onSuccess?.Invoke(creds);
        }
    }

    private IEnumerator TestBalanceAllowance()
{
    Debug.Log("TestBalanceAllowance() entered");

    string apiKey = PlayerPrefs.GetString("PM_APIKEY", "");
    string secret = PlayerPrefs.GetString("PM_SECRET", "");
    string pass = PlayerPrefs.GetString("PM_PASSPHRASE", "");

    Debug.Log($"Loaded creds lengths: apiKey={apiKey.Length}, secret={secret.Length}, pass={pass.Length}");

    if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(pass))
    {
        Debug.LogError("No saved creds. Run derive/create first.");
        yield break;
    }

    long serverTs = 0;
    yield return StartCoroutine(GetServerTime(ts => serverTs = ts));
    if (serverTs <= 0)
    {
        Debug.LogError("Cannot get server time for L2 test");
        yield break;
    }
    string tsString = serverTs.ToString();

    // signature_type: 0 = EOA, 1 = Polymarket proxy, 2 = Safe (1 and 2 also need a funder address).
    int signatureType = 0;

    // The query string is sent but not signed: the signed path is "/balance-allowance".
    string requestPathForSig = "/balance-allowance";

    string query = $"asset_type=COLLATERAL&signature_type={signatureType}";
    string requestPathForHttp = requestPathForSig + "?" + query;
    string url = clobHost + requestPathForHttp;

    const string method = "GET";

    // GET has no body to sign.
    string l2Sig = PolymarketL2Auth.BuildL2Signature(
        secret: secret,
        timestamp: tsString,
        method: method,
        requestPath: requestPathForSig,
        bodyOrNull: null
    );

    Debug.Log($"L2 request: {method} {url}");
    Debug.Log($"L2 signed path: {requestPathForSig}");
    Debug.Log($"L2 timestamp: {tsString}");
    Debug.Log($"L2 signature (len={l2Sig.Length})");

    using (var req = UnityEngine.Networking.UnityWebRequest.Get(url))
    {
        // signerAddressCached is the EOA address used for the L1 signature
        req.SetRequestHeader("POLY_ADDRESS", signerAddressCached);
        req.SetRequestHeader("POLY_API_KEY", apiKey);
        req.SetRequestHeader("POLY_PASSPHRASE", pass);
        req.SetRequestHeader("POLY_TIMESTAMP", tsString);
        req.SetRequestHeader("POLY_SIGNATURE", l2Sig);

        yield return req.SendWebRequest();

        Debug.Log($"BalanceAllowance code={req.responseCode} err={req.error} body={req.downloadHandler?.text}");
    }
}

private IEnumerator SendL2(
    string method,
    string requestPathForSig, // e.g. "/balance-allowance"
    string requestPathForHttp, // e.g. "/balance-allowance?asset_type=COLLATERAL&signature_type=0"
    string bodyJsonOrNull,
    Action<long, string> onDone)
{
    string apiKey = PlayerPrefs.GetString("PM_APIKEY", "");
    string secret = PlayerPrefs.GetString("PM_SECRET", "");
    string pass = PlayerPrefs.GetString("PM_PASSPHRASE", "");

    if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(pass))
    {
        onDone?.Invoke(0, "Missing api creds");
        yield break;
    }

    long serverTs = 0;
    yield return StartCoroutine(GetServerTime(ts => serverTs = ts));
    if (serverTs <= 0)
    {
        onDone?.Invoke(0, "Cannot get server time");
        yield break;
    }
    string tsString = serverTs.ToString();

    string l2Sig = PolymarketL2Auth.BuildL2Signature(
        secret: secret,
        timestamp: tsString,
        method: method,
        requestPath: requestPathForSig,
        bodyOrNull: bodyJsonOrNull
    );

    string url = clobHost + requestPathForHttp;

    UnityWebRequest req;
    if (method == "GET")
    {
        req = UnityWebRequest.Get(url);
    }
    else
    {
        req = new UnityWebRequest(url, method);
        req.downloadHandler = new DownloadHandlerBuffer();

        if (bodyJsonOrNull != null)
        {
            byte[] raw = Encoding.UTF8.GetBytes(bodyJsonOrNull);
            req.uploadHandler = new UploadHandlerRaw(raw);
            req.SetRequestHeader("Content-Type", "application/json");
        }
    }

    req.SetRequestHeader("POLY_ADDRESS", signerAddressCached);
    req.SetRequestHeader("POLY_API_KEY", apiKey);
    req.SetRequestHeader("POLY_PASSPHRASE", pass);
    req.SetRequestHeader("POLY_TIMESTAMP", tsString);
    req.SetRequestHeader("POLY_SIGNATURE", l2Sig);

    yield return req.SendWebRequest();

    onDone?.Invoke(req.responseCode, req.downloadHandler?.text);
}

    private void SaveCredsToPlayerPrefs(ApiCredsResponse creds)
    {
        // TEST ONLY: PlayerPrefs is not secure storage.
        PlayerPrefs.SetString("PM_APIKEY", creds.apiKey);
        PlayerPrefs.SetString("PM_SECRET", creds.secret);
        PlayerPrefs.SetString("PM_PASSPHRASE", creds.passphrase);
        PlayerPrefs.Save();

        if (logAll)
            Debug.Log("Saved creds to PlayerPrefs (TEST ONLY).");
    }

public string GetSignerAddress()
{
    return signerAddressCached;
}

// TEST ONLY: exposes the private key to other classes.
public string GetPrivateKeyHex()
{
    return privateKeyHex;
}

public string GetClobHost()
{
    return clobHost;
}

public int GetChainId()
{
    return chainId;
}

}
