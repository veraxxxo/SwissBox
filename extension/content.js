document.addEventListener("click", (e) => {
  const a = e.target && e.target.closest ? e.target.closest('a[href^="magnet:"]') : null;
  if (!a) return;

  e.preventDefault();
  e.stopPropagation();

  chrome.runtime.sendMessage({ type: "swissbox-magnet", uri: a.href }, (resp) => {
    if (!resp || !resp.ok) {
      // aria2 недоступен — даём браузеру попробовать как обычно, чтобы ссылка не умерла молча
      window.location.href = a.href;
    }
  });
}, true);
