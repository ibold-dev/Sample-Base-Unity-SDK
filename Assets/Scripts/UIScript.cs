using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;

public class BaseSDKController : MonoBehaviour
{
    [Header("SDK Reference")]
    [SerializeField] private BaseSDKWrapper sdkWrapper;

    [Header("UI Elements")]
    [SerializeField] private Button connectButton;
    [SerializeField] private Button getSubAccountButton;
    [SerializeField] private InputField recipientInput;
    [SerializeField] private InputField amountInput;
    [SerializeField] private Button sendTransactionButton;
    [SerializeField] private Button sendBatchButton;
    [SerializeField] private Text outputText;

    [Header("Loading Indicators")]
    [SerializeField] private GameObject loadingPanel; // Optional loading indicator

    private bool isWebGL = false;

    void Awake()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        isWebGL = true;
#endif
    }

    void Start()
    {
        if (!isWebGL)
        {
            LogWarning("BaseSDKController is only supported on WebGL builds.");
            enabled = false;
            return;
        }

        if (sdkWrapper == null)
        {
            LogError("SDK Wrapper reference not assigned!");
            enabled = false;
            return;
        }

        // Initialize UI
        SetupUI();
        UpdateUIState();
    }

    void SetupUI()
    {
        // Setup button listeners
        connectButton.onClick.AddListener(OnConnectButtonClicked);
        getSubAccountButton.onClick.AddListener(OnGetSubAccountButtonClicked);
        sendTransactionButton.onClick.AddListener(OnSendTransactionButtonClicked);
        sendBatchButton.onClick.AddListener(OnSendBatchTransactionButtonClicked);

        // Initial state - all disabled except connect if SDK is ready
        connectButton.interactable = false;
        getSubAccountButton.interactable = false;
        sendTransactionButton.interactable = false;
        sendBatchButton.interactable = false;

        if (loadingPanel != null)
        {
            loadingPanel.SetActive(true);
        }

        Log("Initializing SDK...");
    }

    void UpdateUIState()
    {
        if (sdkWrapper == null) return;

        bool isInitialized = sdkWrapper.IsInitialized();
        bool isConnected = sdkWrapper.GetConnectedAddresses().Length > 0;
        bool hasSubAccount = !string.IsNullOrEmpty(sdkWrapper.GetSubAccountAddress());

        // Update button states
        connectButton.interactable = isInitialized && !isConnected;
        getSubAccountButton.interactable = isConnected && !hasSubAccount;
        sendTransactionButton.interactable = hasSubAccount;
        sendBatchButton.interactable = hasSubAccount;

        // Update button text to reflect state
        if (connectButton != null)
        {
            var buttonText = connectButton.GetComponentInChildren<Text>();
            if (buttonText != null)
            {
                buttonText.text = isConnected ? "Connected" : "Connect Wallet";
            }
        }

        if (getSubAccountButton != null)
        {
            var buttonText = getSubAccountButton.GetComponentInChildren<Text>();
            if (buttonText != null)
            {
                buttonText.text = hasSubAccount ? "Sub-Account Ready" : "Get Sub-Account";
            }
        }
    }

    void OnConnectButtonClicked()
    {
        if (sdkWrapper == null) return;
        
        Log("Connecting wallet...");
        connectButton.interactable = false;
        
        sdkWrapper.ConnectWalletAsync();
    }

    void OnGetSubAccountButtonClicked()
    {
        if (sdkWrapper == null) return;
        
        Log("Fetching sub-account...");
        getSubAccountButton.interactable = false;
        
        sdkWrapper.GetSubAccountAsync();
    }

    void OnSendTransactionButtonClicked()
    {
        if (sdkWrapper == null) return;

        string recipient = recipientInput.text.Trim();
        if (!IsValidAddress(recipient))
        {
            LogError("Invalid recipient address! Must be 42 characters starting with 0x");
            return;
        }

        if (!float.TryParse(amountInput.text.Trim(), out float amount) || amount <= 0)
        {
            LogError("Invalid amount! Must be a positive number.");
            return;
        }

        Log($"Sending {amount} USDC to {recipient}...");
        sendTransactionButton.interactable = false;

        var calls = new List<BaseSDKWrapper.TransactionCall>
        {
            CreateUSDCTransferCall(recipient, amount)
        };

        sdkWrapper.SendTransactionAsync(calls);
    }

    void OnSendBatchTransactionButtonClicked()
    {
        if (sdkWrapper == null) return;

        string recipient = recipientInput.text.Trim();
        if (!IsValidAddress(recipient))
        {
            LogError("Invalid recipient address! Must be 42 characters starting with 0x");
            return;
        }

        if (!float.TryParse(amountInput.text.Trim(), out float amount) || amount <= 0)
        {
            LogError("Invalid amount! Must be a positive number.");
            return;
        }

        Log($"Sending batch: 2x {amount} USDC to {recipient}...");
        sendBatchButton.interactable = false;

        var calls = new List<BaseSDKWrapper.TransactionCall>
        {
            CreateUSDCTransferCall(recipient, amount),
            CreateUSDCTransferCall(recipient, amount)
        };

        sdkWrapper.SendTransactionAsync(calls);
    }

    private BaseSDKWrapper.TransactionCall CreateUSDCTransferCall(string recipient, float amount)
    {
        // USDC has 6 decimals
        long amountInWei = (long)(amount * 1e6);
        
        // ERC20 transfer function signature: transfer(address,uint256)
        string functionSignature = "a9059cbb"; // transfer(address,uint256)
        
        // Remove 0x prefix and pad address to 32 bytes (64 hex chars)
        string recipientHex = recipient.Substring(2).ToLower().PadLeft(64, '0');
        
        // Convert amount to hex and pad to 32 bytes
        string amountHex = amountInWei.ToString("x").PadLeft(64, '0');
        
        // Combine: 0x + function signature + padded address + padded amount
        string data = $"0x{functionSignature}{recipientHex}{amountHex}";
        
        // USDC contract address on Base Sepolia
        return new BaseSDKWrapper.TransactionCall 
        { 
            to = "0x036CbD53842c5426634e7929541eC2318f3dCF7e", 
            data = data 
        };
    }

    private bool IsValidAddress(string address)
    {
        if (string.IsNullOrEmpty(address)) return false;
        if (address.Length != 42) return false;
        if (!address.StartsWith("0x")) return false;
        
        // Check if all characters after 0x are valid hex
        for (int i = 2; i < address.Length; i++)
        {
            char c = address[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
            {
                return false;
            }
        }
        
        return true;
    }

    // Event Handlers for SDK events
    void OnEnable()
    {
        if (!isWebGL || sdkWrapper == null) return;

        // Subscribe to all SDK events
        sdkWrapper.OnSDKReady += HandleSDKReady;
        sdkWrapper.OnWalletReady += HandleWalletReady;
        sdkWrapper.OnSubAccountReady += HandleSubAccountReady;
        sdkWrapper.OnTransactionSent += HandleTransactionSent;
    }

    void OnDisable()
    {
        if (!isWebGL || sdkWrapper == null) return;

        // Unsubscribe from all SDK events
        sdkWrapper.OnSDKReady -= HandleSDKReady;
        sdkWrapper.OnWalletReady -= HandleWalletReady;
        sdkWrapper.OnSubAccountReady -= HandleSubAccountReady;
        sdkWrapper.OnTransactionSent -= HandleTransactionSent;
    }

    private void HandleSDKReady(bool success)
    {
        if (loadingPanel != null)
        {
            loadingPanel.SetActive(false);
        }

        if (success)
        {
            Log("✅ SDK initialized successfully!");
            Log("Click 'Connect Wallet' to begin.");
        }
        else
        {
            LogError("❌ SDK initialization failed!");
        }

        UpdateUIState();
    }

    private void HandleWalletReady(string[] addresses)
    {
        if (addresses == null || addresses.Length == 0)
        {
            LogError("❌ Wallet connection failed!");
            connectButton.interactable = true;
        }
        else
        {
            Log($"✅ Wallet connected!");
            Log($"Universal Address: {addresses[0]}");
            
            if (addresses.Length > 1)
            {
                Log($"Sub-Account: {addresses[1]}");
            }
        }

        UpdateUIState();
    }

    private void HandleSubAccountReady(string address)
    {
        if (string.IsNullOrEmpty(address))
        {
            LogError("❌ Sub-account retrieval failed!");
            getSubAccountButton.interactable = true;
        }
        else
        {
            Log($"✅ Sub-account ready: {address}");
            Log("You can now send transactions!");
        }

        UpdateUIState();
    }

    private void HandleTransactionSent(string txHash)
    {
        if (string.IsNullOrEmpty(txHash))
        {
            LogError("❌ Transaction failed!");
        }
        else
        {
            Log($"✅ Transaction successful!");
            Log($"Hash: {txHash}");
            Log($"View on BaseScan: https://sepolia.basescan.org/tx/{txHash}");
        }

        // Re-enable transaction buttons
        sendTransactionButton.interactable = true;
        sendBatchButton.interactable = true;

        UpdateUIState();
    }

    // Logging helpers with timestamps and colors
    private void Log(string message)
    {
        if (outputText == null) return;
        outputText.text += $"\n<color=#00FF00>[{DateTime.Now:HH:mm:ss}]</color> {message}";
        ScrollToBottom();
    }

    private void LogWarning(string message)
    {
        if (outputText == null) return;
        outputText.text += $"\n<color=yellow>[{DateTime.Now:HH:mm:ss}] ⚠ {message}</color>";
        ScrollToBottom();
    }

    private void LogError(string message)
    {
        if (outputText == null) return;
        outputText.text += $"\n<color=red>[{DateTime.Now:HH:mm:ss}] ❌ {message}</color>";
        ScrollToBottom();
    }

    private void ScrollToBottom()
    {
        // If output text is in a ScrollRect, scroll to bottom
        if (outputText != null)
        {
            var scrollRect = outputText.GetComponentInParent<ScrollRect>();
            if (scrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 0f;
            }
        }
    }

    // Public helper method to clear output log
    public void ClearLog()
    {
        if (outputText != null)
        {
            outputText.text = "=== Base SDK Log ===";
        }
    }

    // Public helper method to get current state
    public string GetSDKState()
    {
        if (sdkWrapper == null) return "SDK not assigned";

        bool isInit = sdkWrapper.IsInitialized();
        var addresses = sdkWrapper.GetConnectedAddresses();
        string subAccount = sdkWrapper.GetSubAccountAddress();

        return $"Initialized: {isInit}\n" +
               $"Connected: {addresses.Length > 0}\n" +
               $"Sub-Account: {(string.IsNullOrEmpty(subAccount) ? "Not ready" : "Ready")}";
    }
}