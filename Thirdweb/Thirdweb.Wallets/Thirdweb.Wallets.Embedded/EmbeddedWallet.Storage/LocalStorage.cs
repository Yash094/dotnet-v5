using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Security;
using System.Threading.Tasks;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Thirdweb.EWS
{
    internal abstract class LocalStorageBase
    {
        internal abstract LocalStorage.DataStorage Data { get; }
        internal abstract LocalStorage.SessionStorage Session { get; }

        internal abstract Task RemoveAuthTokenAsync();
        internal abstract Task RemoveSessionAsync();
        internal abstract Task SaveDataAsync(LocalStorage.DataStorage data);
        internal abstract Task SaveSessionAsync(string sessionId, bool isKmsWallet);
    }

    internal partial class LocalStorage : LocalStorageBase
    {
        internal override DataStorage Data => storage.Data;
        internal override SessionStorage Session => storage.Session;
        private readonly Storage storage;
        private readonly string filePath;

        internal LocalStorage(string clientId)
        {
            string directory;
#if UNITY_5_3_OR_NEWER
                directory = Application.persistentDataPath;
#else
            directory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Console.WriteLine($"Embedded Wallet Storage: Using '{directory}'");
#endif
            directory = Path.Combine(directory, "EWS");
            Directory.CreateDirectory(directory);
            filePath = Path.Combine(directory, $"{clientId}.txt");
            try
            {
                byte[] json = File.ReadAllBytes(filePath);
                DataContractJsonSerializer serializer = new(typeof(Storage));
                MemoryStream fin = new(json);
                storage = (Storage)serializer.ReadObject(fin);
            }
            catch (Exception)
            {
                storage = new Storage();
            }
        }

        internal override Task RemoveAuthTokenAsync()
        {
            return UpdateDataAsync(() =>
            {
                if (storage.Data?.AuthToken != null)
                {
                    storage.Data.ClearAuthToken();
                    return true;
                }
                return false;
            });
        }

        private async Task<bool> UpdateDataAsync(Func<bool> fn)
        {
            if (fn())
            {
                DataContractJsonSerializer serializer = new(typeof(Storage));
                MemoryStream fout = new();
                serializer.WriteObject(fout, storage);
                await File.WriteAllBytesAsync(filePath, fout.ToArray());
                return true;
            }
            return false;
        }

        internal override Task SaveDataAsync(DataStorage data)
        {
            return UpdateDataAsync(() =>
            {
                storage.Data = data;
                return true;
            });
        }

        internal override Task SaveSessionAsync(string sessionId, bool isKmsWallet)
        {
            return UpdateDataAsync(() =>
            {
                storage.Session = new SessionStorage(sessionId, isKmsWallet);
                return true;
            });
        }

        internal override Task RemoveSessionAsync()
        {
            return UpdateDataAsync(() =>
            {
                storage.Session = null;
                return true;
            });
        }
    }
}
