using System.Numerics;
using Nethereum.ABI.EIP712;
using Nethereum.RPC.Eth.DTOs;

namespace Thirdweb
{
    public interface IThirdwebAccount
    {
        public ThirdwebAccountType AccountType { get; }
        public Task Connect();
        public Task<string> GetAddress();
        public Task<string> EthSign(string message);
        public Task<string> PersonalSign(byte[] rawMessage);
        public Task<string> PersonalSign(string message);
        public Task<string> SignTypedDataV4(string json);
        public Task<string> SignTypedDataV4<T, TDomain>(T data, TypedData<TDomain> typedData);
        public Task<string> SignTransaction(TransactionInput transaction, BigInteger chainId);
        public Task<bool> IsConnected();
        public Task Disconnect();
    }

    public enum ThirdwebAccountType
    {
        PrivateKeyAccount,
        SmartAccount
    }
}