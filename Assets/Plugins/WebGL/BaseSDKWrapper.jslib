mergeInto(LibraryManager.library, {
  $BaseSDK: {
    // Supported networks
    NETWORKS: {
      base: {
        chainId: "0x" + (8453).toString(16),
        name: "Base",
        rpcUrl: "https://mainnet.base.org",
      },
      basesepolia: {
        chainId: "0x" + (84532).toString(16),
        name: "Base Sepolia",
        rpcUrl: "https://sepolia.base.org",
      },
    },

    // Global state
    sdk: null,
    provider: null,
    universalAddress: null,
    subAccountAddress: null,
    currentNetwork: null,

    // Logging function
    log: function (message, type) {
      if (type === undefined) type = "info";
      console.log(`[${type.toUpperCase()}] ${message}`);
    },

    // Load the SDK dynamically
    loadBaseAccountSDK: function () {
      return new Promise((resolve, reject) => {
        if (typeof window.createBaseAccountSDK !== "undefined") {
          resolve(window.createBaseAccountSDK);
        } else {
          const script = document.createElement("script");
          script.src =
            "https://unpkg.com/@base-org/account/dist/base-account.min.js";
          script.onload = () => resolve(window.createBaseAccountSDK);
          script.onerror = (e) => reject(new Error(`Failed to load SDK: ${e}`));
          document.head.appendChild(script);
        }
      });
    },
  },

  // Initialize SDK with network and configuration
  InitSDK__deps: ["$BaseSDK"],
  InitSDK: function (configJson, network, customRpcUrl) {
    const configStr = UTF8ToString(configJson);
    const networkStr = UTF8ToString(network);
    const customRpcUrlStr = customRpcUrl ? UTF8ToString(customRpcUrl) : null;

    BaseSDK.log(`Initializing SDK for network: ${networkStr}...`);

    const config = JSON.parse(configStr);
    const networkKey = networkStr.toLowerCase();

    if (!BaseSDK.NETWORKS[networkKey]) {
      BaseSDK.log(
        `Unsupported network: ${networkStr}. Use 'base' or 'basesepolia'.`,
        "error"
      );
      return;
    }

    BaseSDK.currentNetwork = Object.assign({}, BaseSDK.NETWORKS[networkKey]);

    if (customRpcUrlStr) {
      BaseSDK.log(`Overriding RPC URL with: ${customRpcUrlStr}`);
      BaseSDK.currentNetwork.rpcUrl = customRpcUrlStr;
    }

    BaseSDK.log("Loading Base Account SDK...");

    BaseSDK.loadBaseAccountSDK()
      .then((createSDK) => {
        const sdkConfig = {
          appName: config.appName,
          subAccounts: config.subAccounts || {
            creation: "on-connect",
            defaultAccount: "sub",
          },
          paymaster: config.paymaster || {
            url: "https://paymaster.base.org",
            policy: "VERIFYING_PAYMASTER",
          },
        };

        BaseSDK.sdk = createSDK(sdkConfig);
        BaseSDK.provider = BaseSDK.sdk.getProvider();
        BaseSDK.log(
          `SDK initialized successfully for ${BaseSDK.currentNetwork.name}!`
        );

        // Notify Unity that initialization is complete
        SendMessage("BaseSDKWrapper", "OnSDKInitialized", "success");
      })
      .catch((error) => {
        BaseSDK.log(`Error initializing SDK: ${error.message}`, "error");
        SendMessage("BaseSDKWrapper", "OnSDKInitialized", "failed");
      });
  },

  // Connect wallet and retrieve accounts
  ConnectWallet__deps: ["$BaseSDK"],
  ConnectWallet: function () {
    if (!BaseSDK.provider) {
      BaseSDK.log("SDK not initialized. Call InitSDK first.", "error");
      SendMessage("BaseSDKWrapper", "OnWalletConnected", "");
      return;
    }

    BaseSDK.log("Requesting accounts...");

    BaseSDK.provider
      .request({ method: "eth_requestAccounts" })
      .then((addresses) => {
        BaseSDK.universalAddress = addresses[0];
        BaseSDK.subAccountAddress = addresses[1];

        BaseSDK.log(`✅ Connected!`);
        BaseSDK.log(`Universal Address: ${BaseSDK.universalAddress}`);

        if (BaseSDK.subAccountAddress) {
          BaseSDK.log(`SubAccount Address: ${BaseSDK.subAccountAddress}`);
        }

        // Set up event listeners
        BaseSDK.provider.on("accountsChanged", (accounts) => {
          BaseSDK.log(`Accounts changed: ${accounts.join(", ")}`);
          BaseSDK.universalAddress = accounts[0];
          BaseSDK.subAccountAddress = accounts[1] || null;
        });

        BaseSDK.provider.on("chainChanged", (chainId) => {
          BaseSDK.log(`Chain changed: ${chainId}`);
          if (
            BaseSDK.currentNetwork &&
            chainId !== BaseSDK.currentNetwork.chainId
          ) {
            BaseSDK.log(
              `Warning: Chain ID mismatch. Expected ${BaseSDK.currentNetwork.chainId}`,
              "warning"
            );
          }
        });

        // Send addresses back to Unity as JSON
        const addressesJson = JSON.stringify(addresses);
        SendMessage("BaseSDKWrapper", "OnWalletConnected", addressesJson);
      })
      .catch((error) => {
        BaseSDK.log(`Error connecting wallet: ${error.message}`, "error");
        SendMessage("BaseSDKWrapper", "OnWalletConnected", "");
      });
  },

  // Get sub-account
  GetSubAccount__deps: ["$BaseSDK"],
  GetSubAccount: function () {
    if (!BaseSDK.provider || !BaseSDK.universalAddress) {
      BaseSDK.log("Wallet not connected or SDK not initialized.", "error");
      SendMessage("BaseSDKWrapper", "OnSubAccountRetrieved", "");
      return;
    }

    BaseSDK.log("Fetching SubAccount...");

    BaseSDK.provider
      .request({
        method: "wallet_getSubAccounts",
        params: [
          { account: BaseSDK.universalAddress, domain: window.location.origin },
        ],
      })
      .then((result) => {
        if (result.subAccounts && result.subAccounts.length > 0) {
          BaseSDK.subAccountAddress = result.subAccounts[0].address;
          BaseSDK.log(`✅ SubAccount found: ${BaseSDK.subAccountAddress}`);
          SendMessage(
            "BaseSDKWrapper",
            "OnSubAccountRetrieved",
            BaseSDK.subAccountAddress
          );
        } else {
          BaseSDK.log("No SubAccount exists. Creating one...");
          return BaseSDK.provider
            .request({
              method: "wallet_addSubAccount",
              params: [{ version: "1" }],
            })
            .then(() => {
              return BaseSDK.provider.request({
                method: "wallet_getSubAccounts",
                params: [
                  {
                    account: BaseSDK.universalAddress,
                    domain: window.location.origin,
                  },
                ],
              });
            })
            .then((newResult) => {
              if (newResult.subAccounts && newResult.subAccounts.length > 0) {
                BaseSDK.subAccountAddress = newResult.subAccounts[0].address;
                BaseSDK.log(
                  `✅ SubAccount created: ${BaseSDK.subAccountAddress}`
                );
                SendMessage(
                  "BaseSDKWrapper",
                  "OnSubAccountRetrieved",
                  BaseSDK.subAccountAddress
                );
              }
            });
        }
      })
      .catch((error) => {
        BaseSDK.log(`Error getting SubAccount: ${error.message}`, "error");
        SendMessage("BaseSDKWrapper", "OnSubAccountRetrieved", "");
      });
  },

  // Send transaction
  SendTransaction__deps: ["$BaseSDK"],
  SendTransaction: function (callsJson, chainIdOverride) {
    if (!BaseSDK.provider || !BaseSDK.subAccountAddress) {
      BaseSDK.log(
        "SubAccount not available. Call ConnectWallet and GetSubAccount first.",
        "error"
      );
      SendMessage("BaseSDKWrapper", "OnTransactionComplete", "");
      return;
    }

    const callsStr = UTF8ToString(callsJson);
    const chainIdStr = chainIdOverride ? UTF8ToString(chainIdOverride) : null;
    const calls = JSON.parse(callsStr);

    if (!calls || calls.length === 0) {
      BaseSDK.log("No calls provided!", "error");
      SendMessage("BaseSDKWrapper", "OnTransactionComplete", "");
      return;
    }

    BaseSDK.log(
      `Sending transaction with ${calls.length} calls from SubAccount...`
    );

    // Validate calls
    for (const call of calls) {
      if (!call.to || !call.to.match(/^0x[a-fA-F0-9]{40}$/)) {
        BaseSDK.log(`Invalid 'to' address in call: ${call.to}`, "error");
        SendMessage("BaseSDKWrapper", "OnTransactionComplete", "");
        return;
      }
      if (!call.data || !/^(0x)?[a-fA-F0-9]*$/.test(call.data)) {
        BaseSDK.log(`Invalid 'data' in call: ${call.data}`, "error");
        SendMessage("BaseSDKWrapper", "OnTransactionComplete", "");
        return;
      }
    }

    BaseSDK.provider
      .request({ method: "eth_chainId" })
      .then((currentChainId) => {
        const chainId = chainIdStr || currentChainId;

        if (
          !chainId ||
          !Object.values(BaseSDK.NETWORKS).some(
            (net) => net.chainId === chainId
          )
        ) {
          BaseSDK.log(
            `Unsupported chainId: ${chainId}. Use Base or Base Sepolia.`,
            "error"
          );
          SendMessage("BaseSDKWrapper", "OnTransactionComplete", "");
          return;
        }

        const txParams = {
          from: BaseSDK.subAccountAddress,
          chainId: chainId,
          version: "1",
          calls: calls,
        };

        BaseSDK.log(`Transaction params: ${JSON.stringify(txParams)}`);

        return BaseSDK.provider.request({
          method: "wallet_sendCalls",
          params: [txParams],
        });
      })
      .then((txHash) => {
        BaseSDK.log(`✅ Transaction sent!`);
        BaseSDK.log(`Transaction Hash: ${txHash}`);
        BaseSDK.log(
          `View on BaseScan: https://sepolia.basescan.org/tx/${txHash}`
        );
        SendMessage("BaseSDKWrapper", "OnTransactionComplete", txHash);
      })
      .catch((error) => {
        BaseSDK.log(`Error sending transaction: ${error.message}`, "error");
        console.error(error);
        SendMessage("BaseSDKWrapper", "OnTransactionComplete", "");
      });
  },

  // Get current network information
  GetCurrentNetworkJSON__deps: ["$BaseSDK"],
  GetCurrentNetworkJSON: function () {
    if (!BaseSDK.currentNetwork) {
      return null;
    }
    const json = JSON.stringify(BaseSDK.currentNetwork);
    const bufferSize = lengthBytesUTF8(json) + 1;
    const buffer = _malloc(bufferSize);
    stringToUTF8(json, buffer, bufferSize);
    return buffer;
  },
});