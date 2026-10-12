using System;
using UnityEngine;

#region Data API Trades
[Serializable]
public class DataApiTrade
{
    public string proxyWallet;
    public string side;         
    public string asset;
    public string conditionId;   
    public double size;
    public double price;
    public long timestamp;       
    public string outcome;
    public int outcomeIndex;
    public string transactionHash;
}

[Serializable]
public class DataApiTradesResponse
{
    public DataApiTrade[] trades;
}

#endregion
#region Data API Positions
// One item of GET /positions. Only the fields we use; JsonUtility ignores the rest.
[Serializable]
public class DataApiPosition
{
    public string asset;   // token id of the outcome
    public double size;    // shares held
}

[Serializable]
public class DataApiPositionsResponse
{
    public DataApiPosition[] positions;
}

#endregion
#region POLYMARKET MODELS
[Serializable]
public class PolymarketEvent
{
    public string id;
    public string title;
    public string ticker;
    public string slug;
    public string startDate;
    public string endDate;
    public bool active;
    public bool closed;
    public bool archived;
    public Market[] markets;
}

[Serializable]
public class Market
{
    public string id;
    public string question;
    public string clobTokenIds;
    public string conditionId;

    public bool active;
    public bool closed;

    public string[] GetClobTokenIdsNormalized()
    {
        if (string.IsNullOrEmpty(clobTokenIds)) return Array.Empty<string>();
        if (!clobTokenIds.TrimStart().StartsWith("[")) return Array.Empty<string>();

        try
        {
            string wrapped = "{\"a\":" + clobTokenIds + "}";
            return JsonUtility.FromJson<StringArrayWrapper>(wrapped)?.a ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}

[Serializable]
public class StringArrayWrapper
{
    public string[] a;
}

[Serializable]
public class GammaEventsResponse
{
    public PolymarketEvent[] events;
}

[Serializable]
public class GammaMarketsResponse
{
    public Market[] markets;
}
#endregion

#region CLOB MODELS
[Serializable]
public class ClobOrder
{
    public string price;
    public string size;
}
[Serializable]
public class ClobTrade
{
    public string price;
    public string size;
    public long timestamp;
}

[Serializable]
public class ClobTradesResponse
{
    public ClobTrade[] trades;
}

[Serializable]
public class ClobOrderBook
{
    public string market;
    public string asset_id;
    public ClobOrder[] bids;
    public ClobOrder[] asks;
}
#endregion

#region APPLICATION MODELS
[Serializable]
public class ActiveMarketData
{
    public string eventId;
    public string eventTitle;
    public DateTime sessionStartTime;
    public DateTime sessionEndTime;
    public double priceToBeatChainlink;
    public double currentBtcPrice;
    public double bidUp;
    public double askUp;
    public double bidDown;
    public double askDown;
    public double volumeThis15m;
    public double spreadFromStart;
    public DateTime lastUpdateTime;
}
#endregion

#region EXTERNAL API MODELS
[Serializable]
public class BinancePriceResponse
{
    public string symbol;
    public string price;
}
#endregion