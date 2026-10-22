const DEFAULT_RPC = "http://127.0.0.1:16800/jsonrpc";

async function getSettings() {
  const { enabled = true, rpcUrl = DEFAULT_RPC, minSizeMb = 0 } =
    await chrome.storage.local.get(["enabled", "rpcUrl", "minSizeMb"]);
  return { enabled, rpcUrl, minSizeMb };
}

async function sendToAria2(uri, rpcUrl, headers) {
  const options = {};
  if (headers && headers.length) options.header = headers;
  const body = {
    jsonrpc: "2.0",
    id: "swissbox",
    method: "aria2.addUri",
    params: [[uri], options]
  };
  try {
    const res = await fetch(rpcUrl, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    });
    return res.ok;
  } catch (e) {
    return false;
  }
}

chrome.downloads.onCreated.addListener(async (item) => {
  const { enabled, rpcUrl, minSizeMb } = await getSettings();
  if (!enabled) return;
  if (!item.url || item.url.startsWith("blob:") || item.url.startsWith("data:")) return;
  if (minSizeMb > 0 && item.fileSize > 0 && item.fileSize < minSizeMb * 1024 * 1024) return;

  let headers = [];
  try {
    const cookies = await chrome.cookies.getAll({ url: item.url });
    if (cookies.length) {
      headers.push("Cookie: " + cookies.map(c => `${c.name}=${c.value}`).join("; "));
    }
  } catch (e) {
    /* ignore — без куки просто попробуем без авторизации */
  }

  const ok = await sendToAria2(item.url, rpcUrl, headers);
  if (ok) {
    try { await chrome.downloads.cancel(item.id); } catch (e) {}
    try { await chrome.downloads.erase({ id: item.id }); } catch (e) {}
  }
});

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (msg && msg.type === "swissbox-magnet") {
    getSettings().then(({ enabled, rpcUrl }) => {
      if (!enabled) { sendResponse({ ok: false }); return; }
      sendToAria2(msg.uri, rpcUrl, []).then(ok => sendResponse({ ok }));
    });
    return true; // отвечаем асинхронно
  }
});
