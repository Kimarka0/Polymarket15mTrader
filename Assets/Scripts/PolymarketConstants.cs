public static class PolymarketConstants
{
    public const int CHAIN_ID_POLYGON = 137;

    // Amounts are integers in micro units: 1 share or 1 USDC = 1_000_000.
    public const long AMOUNT_SCALE = 1_000_000;

    // EIP-712 domain for orders on the regular CTF Exchange (not neg-risk).
    public const string ORDER_DOMAIN_NAME = "Polymarket CTF Exchange";
    public const string ORDER_DOMAIN_VERSION = "1";

    // Exchange contract (Polygon). Neg-risk markets use a different verifyingContract.
    public const string EXCHANGE_VERIFYING_CONTRACT = "0x4bFb41d5B3570DeFd03C39a9A4D8dE6Bd8B8982E";

    public const string ZERO_ADDRESS = "0x0000000000000000000000000000000000000000";
    public const string ZERO_BYTES32 = "0x0000000000000000000000000000000000000000000000000000000000000000";

    public const string CLOB_HOST = "https://clob.polymarket.com";
}
