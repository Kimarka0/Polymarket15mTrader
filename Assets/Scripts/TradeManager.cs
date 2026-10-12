using System;

public interface TradeManager
{
    event Action MarketStartedEvent;
    event Action MarketFinishedEvent;

    event Action SellComplitedEvent;
    event Action BuyComplitedEvent;

    void Sell();
    void Buy(float value);

    MarketManagerStates GetCurrentStates();
    event Action<MarketManagerStates> StateChangesEvent;

    event Action<MarketDataTik> MarketDataTickEvent;
}

public enum MarketManagerStates
{
    WaitInitedManager,
    WaitStartMarket,
    InProgress,
    MarketFinished,
    Error
}

public struct MarketDataTik
{
    public float timeToEnd;
    public float askUp;
    public float askDown;
    public float spread;
    public float bidUp;
    public float bidDown;
    public float volume;
}
