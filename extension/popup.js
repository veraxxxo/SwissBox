const DEFAULT_RPC = "http://127.0.0.1:16800/jsonrpc";

async function load() {
  const { enabled = true, rpcUrl = DEFAULT_RPC, minSizeMb = 0 } =
    await chrome.storage.local.get(["enabled", "rpcUrl", "minSizeMb"]);
  document.getElementById("enabled").checked = enabled;
  document.getElementById("rpcUrl").value = rpcUrl;
  document.getElementById("minSizeMb").value = minSizeMb;
}

document.getElementById("save").addEventListener("click", async () => {
  const enabled = document.getElementById("enabled").checked;
  const rpcUrl = document.getElementById("rpcUrl").value.trim() || DEFAULT_RPC;
  const minSizeMb = parseFloat(document.getElementById("minSizeMb").value) || 0;
  await chrome.storage.local.set({ enabled, rpcUrl, minSizeMb });
  document.getElementById("status").textContent = "Сохранено.";
  setTimeout(() => window.close(), 400);
});

load();
