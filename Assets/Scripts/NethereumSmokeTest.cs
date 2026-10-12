using UnityEngine;
using Nethereum.Signer;
using Nethereum.Util;

public class NethereumSmokeTest : MonoBehaviour
{
    void Start()
    {
        var sha3 = new Sha3Keccack();
        Debug.Log("keccak('hello') = " + sha3.CalculateHash("hello"));

        var key = new EthECKey("0x0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
        Debug.Log("address = " + key.GetPublicAddress());
    }
}
