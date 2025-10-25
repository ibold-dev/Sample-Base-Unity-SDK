using UnityEngine;
using System.Runtime.InteropServices;
using System;
using System.Collections.Generic;

public class BaseSDKWrapper : MonoBehaviour
{
    [Header("SDK Configuration")]
    [SerializeField] private string appName = "My Unity Game";
    [SerializeField] private string network = "basesepolia";
    [SerializeField] private string customRpcUrl = "";
    [SerializeField] private string paymasterUrl = "https://paymaster.base.org";
    [SerializeField] private string paymasterPolicy = "VERIFYING_PAYMASTER";

    [System.Serializable]
    public class TransactionCall
    {
        public string to;
        public string data;
    }

    [System.Serializable]
    private class TransactionCallsList
    {
        public List<TransactionCall> calls;
    }

    [System.Serializable]
    private class SDKConfig
    {
        public string appName;
        public SubAccountsConfig subAccounts;
        public PaymasterConfig paymaster;
    }

    [System.Serializable]
    private class SubAccountsConfig
    {
        public string creation;
        public string defaultAccount;
    }

    [System.Serializable]
    private class PaymasterConfig
    {
        public string url;
        public string policy;
    }

    // State
    private bool isInitialized = false;
    private string[] connectedAddresses = new string[0];
    private string subAccountAddress = null;
    private bool isWebGL = false;

    // Events
    public event Action<string> OnTransactionSent;
    public event Action<bool> OnSDKReady;
    public event Action<string[]> OnWalletReady;
    public event Action<string> OnSubAccountReady;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void InitSDK(string configJson, string network, string customRpcUrl);

    [DllImport("__Internal")]
    private static extern void ConnectWallet();

    [DllImport("__Internal")]
    private static extern void GetSubAccount();

    [DllImport("__Internal")]
    private static extern void SendTransaction(string callsJson, string chainIdOverride);

    [DllImport("__Internal")]
    private static extern string GetCurrentNetworkJSON();
#endif

    void Awake()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        isWebGL = true;
#endif
        if (!isWebGL)
        {
            Debug.LogWarning("BaseSDKWrapper only works in WebGL builds.");
        }
    }

    void Start()
    {
        if (isWebGL)
        {
            InitializeSDK();
        }
    }

    public void InitializeSDK()
    {
        if (!isWebGL)
        {
            Debug.LogError("Cannot initialize SDK on non-WebGL platform.");
            return;
        }

        if (isInitialized)
        {
            Debug.LogWarning("SDK already initialized.");
            return;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        SDKConfig config = new SDKConfig
        {
            appName = appName,
            subAccounts = new SubAccountsConfig
            {
                creation = "on-connect",
                defaultAccount = "sub"
            },
            paymaster = new PaymasterConfig
            {
                url = paymasterUrl,
                policy = paymasterPolicy
            }
        };

        string configJson = JsonUtility.ToJson(config);
        Debug.Log($"Initializing SDK with config: {configJson}");
        
        InitSDK(configJson, network, string.IsNullOrEmpty(customRpcUrl) ? null : customRpcUrl);
#endif
    }

    // Called from JavaScript when SDK is initialized
    private void OnSDKInitialized(string result)
    {
        if (result == "success")
        {
            isInitialized = true;
            Debug.Log("SDK initialized successfully!");
            OnSDKReady?.Invoke(true);

            // Auto-connect wallet after SDK init
            ConnectWalletAsync();
        }
        else
        {
            Debug.LogError("SDK initialization failed!");
            OnSDKReady?.Invoke(false);
        }
    }

    public void ConnectWalletAsync()
    {
        if (!isWebGL)
        {
            Debug.LogError("Cannot connect wallet on non-WebGL platform.");
            return;
        }

        if (!isInitialized)
        {
            Debug.LogError("SDK not initialized. Call InitializeSDK first.");
            return;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        Debug.Log("Connecting wallet...");
        ConnectWallet();
#endif
    }

    // Called from JavaScript when wallet is connected
    private void OnWalletConnected(string addressesJson)
    {
        if (string.IsNullOrEmpty(addressesJson))
        {
            Debug.LogError("Wallet connection failed!");
            connectedAddresses = new string[0];
            OnWalletReady?.Invoke(connectedAddresses);
            return;
        }

        try
        {
            connectedAddresses = JsonHelper.FromJson<string>(addressesJson);
            Debug.Log($"Wallet connected! Addresses: {string.Join(", ", connectedAddresses)}");
            OnWalletReady?.Invoke(connectedAddresses);

            // Auto-retrieve sub-account
            GetSubAccountAsync();
        }
        catch (Exception e)
        {
            Debug.LogError($"Error parsing addresses: {e.Message}");
            connectedAddresses = new string[0];
            OnWalletReady?.Invoke(connectedAddresses);
        }
    }

    public void GetSubAccountAsync()
    {
        if (!isWebGL)
        {
            Debug.LogError("Cannot get sub-account on non-WebGL platform.");
            return;
        }

        if (!isInitialized)
        {
            Debug.LogError("SDK not initialized.");
            return;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        Debug.Log("Retrieving sub-account...");
        GetSubAccount();
#endif
    }

    // Called from JavaScript when sub-account is retrieved
    private void OnSubAccountRetrieved(string address)
    {
        if (string.IsNullOrEmpty(address))
        {
            Debug.LogError("Sub-account retrieval failed!");
            subAccountAddress = null;
        }
        else
        {
            subAccountAddress = address;
            Debug.Log($"Sub-account retrieved: {subAccountAddress}");
        }

        OnSubAccountReady?.Invoke(subAccountAddress);
    }

    public void SendTransactionAsync(List<TransactionCall> calls, string chainIdOverride = null)
    {
        if (!isWebGL)
        {
            Debug.LogError("Cannot send transaction on non-WebGL platform.");
            return;
        }

        if (!isInitialized || string.IsNullOrEmpty(subAccountAddress))
        {
            Debug.LogError("SDK not ready or sub-account not available.");
            return;
        }

        if (calls == null || calls.Count == 0)
        {
            Debug.LogError("No transaction calls provided.");
            return;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // Wrap calls in an object with a "calls" array for proper JSON serialization
        string callsJson = JsonUtility.ToJson(new { calls = calls.ToArray() });
        // Remove the outer wrapper to get just the array
        callsJson = callsJson.Substring(9, callsJson.Length - 10); // Remove {"calls": and }
        
        Debug.Log($"Sending transaction with calls: {callsJson}");
        SendTransaction(callsJson, chainIdOverride);
#endif
    }

    // Called from JavaScript when transaction is complete
    private void OnTransactionComplete(string txHash)
    {
        if (string.IsNullOrEmpty(txHash))
        {
            Debug.LogError("Transaction failed!");
            return;
        }

        Debug.Log($"Transaction successful! Hash: {txHash}");
        OnTransactionSent?.Invoke(txHash);
    }

    public string GetCurrentNetwork()
    {
        if (!isWebGL || !isInitialized)
        {
            return null;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        return GetCurrentNetworkJSON();
#else
        return null;
#endif
    }

    public string[] GetConnectedAddresses()
    {
        return connectedAddresses;
    }

    public string GetSubAccountAddress()
    {
        return subAccountAddress;
    }

    public bool IsInitialized()
    {
        return isInitialized;
    }
}

// Helper class for JSON array deserialization
public static class JsonHelper
{
    public static T[] FromJson<T>(string json)
    {
        Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>("{\"Items\":" + json + "}");
        return wrapper.Items;
    }

    [Serializable]
    private class Wrapper<T>
    {
        public T[] Items;
    }
}