using Nethereum.Util;

public static class Keccak
{
    private static readonly Sha3Keccack sha3 = new Sha3Keccack();

    public static byte[] Keccak256(byte[] input)
    {
        return sha3.CalculateHash(input);
    }
}
