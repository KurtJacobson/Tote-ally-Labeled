const $ = id => document.getElementById(id);

const state = {
  layouts: [],
  layout: null,
  hasLogo: false,
  printerIp: ""
};

// Shown in the preview until the user types something.
const sample = {
  title: "Holiday decorations",
  contents: "String lights\nOrnaments\nTree skirt"
};

// ---------- Talking to the app ----------

async function api(path, options = {}) {
  let response;
  try {
    response = await fetch(path, options);
  } catch {
    throw new Error("Can't reach the Tote Labels app. Make sure it's still running.");
  }

  if (!response.ok) {
    throw new Error((await response.text()) || "Something went wrong. Try again.");
  }

  const type = response.headers.get("content-type") ?? "";
  return type.includes("json") ? response.json() : null;
}

function sendJson(path, body, method = "POST") {
  return api(path, {
    method,
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body)
  });
}

function showStatus(element, message, kind = "") {
  element.textContent = message;
  element.dataset.kind = kind;
}

// ---------- Label size ----------

function renderSizePicker() {
  for (const layout of state.layouts) {
    const option = document.createElement("label");
    option.className = "segment";
    option.innerHTML = `
      <input type="radio" name="size" class="visually-hidden" value="${layout.id}">
      <span><span class="shape" style="aspect-ratio: ${layout.widthInches} / ${layout.heightInches}"></span>${layout.name}</span>`;
    option.querySelector("input").addEventListener("change", () => selectLayout(layout.id));
    $("sizes").append(option);
  }
}

function selectLayout(id) {
  state.layout = state.layouts.find(layout => layout.id === id) ?? state.layouts[0];
  document.querySelector(`input[name="size"][value="${state.layout.id}"]`).checked = true;

  try { localStorage.setItem("toteLabels.size", state.layout.id); } catch { }

  const maxLines = state.layout.contents.maxLines;
  $("contents-hint").textContent = `One item per line, up to ${maxLines} lines`;
  $("size-caption").textContent = `${state.layout.name} label`;

  arrangePreview();
  updatePreview();
}

function rememberedSize() {
  try { return localStorage.getItem("toteLabels.size"); } catch { return null; }
}

// ---------- Preview ----------
// The layout comes from the server in printer dots, so the preview is placed
// with the same numbers the printer uses, as percentages of the label.

const percent = (dots, total) => `${(dots / total) * 100}%`;

function place(element, box) {
  element.hidden = !box;
  if (!box) return;

  const { widthDots, heightDots } = state.layout;
  Object.assign(element.style, {
    left: percent(box.x, widthDots),
    top: percent(box.y, heightDots),
    width: percent(box.width, widthDots),
    height: percent(box.height, heightDots)
  });
}

function placeText(element, block) {
  const lineHeight = block.fontSize + block.lineGap;
  place(element, { x: block.x, y: block.y, width: block.width, height: lineHeight * block.maxLines });

  element.style.fontSize = `${(block.fontSize / state.layout.widthDots) * 100}cqw`;
  element.style.lineHeight = lineHeight / block.fontSize;
  element.style.textAlign = block.align === "C" ? "center" : "left";
}

function arrangePreview() {
  const layout = state.layout;

  // Fit the label in roughly a 380 × 400 px area, keeping its real proportions.
  const pixelsPerInch = Math.min(400 / layout.heightInches, 380 / layout.widthInches);
  const label = $("label");
  label.style.width = `min(100%, ${layout.widthInches * pixelsPerInch}px)`;
  label.style.aspectRatio = `${layout.widthDots} / ${layout.heightDots}`;

  placeText($("preview-title"), layout.title);
  placeText($("preview-contents"), layout.contents);

  const rule = layout.divider;
  place($("preview-divider"), rule && { x: rule.x, y: rule.y, width: rule.width, height: rule.thickness });

  place($("preview-logo"), layout.logo);
  showLogoInPreview();
}

function updatePreview() {
  setPreviewText($("preview-title"), $("title").value.trim(), sample.title);
  setPreviewText($("preview-contents"), $("contents").value.trimEnd(), sample.contents);
  checkFit();
}

function setPreviewText(element, text, placeholder) {
  element.textContent = text || placeholder;
  element.classList.toggle("empty", !text);
}

function checkFit() {
  // Allow half a line of slack so rounding doesn't trigger a false warning.
  const overflows = element =>
    !element.hidden &&
    !element.classList.contains("empty") &&
    element.scrollHeight - element.clientHeight > parseFloat(getComputedStyle(element).fontSize) / 2;

  const parts = [];
  if (overflows($("preview-title"))) parts.push("title");
  if (overflows($("preview-contents"))) parts.push("contents");

  const warning = $("fit-warning");
  warning.hidden = parts.length === 0;
  warning.textContent =
    `The ${parts.join(" and ")} won't fit on a ${state.layout.name} label. ` +
    "Shorten it, or the printer will print lines on top of each other.";
}

// ---------- Printing ----------

async function printLabel() {
  const status = $("status");
  const title = $("title").value.trim();

  if (!title) {
    showStatus(status, "Add a title before printing.", "error");
    $("title").focus();
    return;
  }

  const copies = Math.min(99, Math.max(1, parseInt($("copies").value, 10) || 1));
  $("copies").value = copies;

  $("print").disabled = true;
  showStatus(status, "Sending to the printer…");

  try {
    await sendJson("/api/print", {
      layoutId: state.layout.id,
      title,
      contents: $("contents").value.trimEnd(),
      copies
    });
    showStatus(status, copies === 1 ? "Sent 1 label to the printer." : `Sent ${copies} labels to the printer.`, "ok");
  } catch (error) {
    showStatus(status, error.message, "error");
  } finally {
    $("print").disabled = false;
  }
}

async function checkPrinter() {
  const show = (text, kind = "") => {
    $("printer-status-text").textContent = text;
    $("printer-status").dataset.kind = kind;
  };

  if (!state.printerIp) {
    show("Not set up", "error");
    return;
  }

  show("Checking…");
  try {
    await api("/api/printer/check", { method: "POST" });
    show("Connected", "ok");
  } catch {
    show("Offline", "error");
  }
}

// ---------- Settings ----------

async function openSettings() {
  try {
    const settings = await api("/api/settings");
    $("printer-ip").value = settings.printerIp;
    document.querySelector(`input[name="dpi"][value="${settings.dpi}"]`).checked = true;
    $("top-offset").value = settings.topOffsetMm;
  } catch (error) {
    showStatus($("status"), error.message, "error");
    return;
  }

  for (const id of ["test-status", "logo-status", "calibrate-status", "save-status"]) {
    showStatus($(id), "");
  }
  $("settings").showModal();
}

async function saveSettings(event) {
  event.preventDefault();

  const printerIp = $("printer-ip").value.trim();
  const dpi = Number(document.querySelector('input[name="dpi"]:checked').value);
  const topOffsetMm = Number($("top-offset").value) || 0;

  try {
    await sendJson("/api/settings", { printerIp, dpi, topOffsetMm }, "PUT");
    state.printerIp = printerIp;
    $("settings").close();
    checkPrinter();
  } catch (error) {
    showStatus($("save-status"), error.message, "error");
  }
}

// Test and Calibrate use the address in the box, so it can be tried before saving.
async function sendPrinterCommand(path, statusId, successMessage) {
  const ip = $("printer-ip").value.trim();
  const status = $(statusId);

  if (!ip) {
    showStatus(status, "Enter the printer's IP address first.", "error");
    return;
  }

  showStatus(status, "Contacting the printer…");
  try {
    await api(`${path}?ip=${encodeURIComponent(ip)}`, { method: "POST" });
    showStatus(status, successMessage, "ok");
  } catch (error) {
    showStatus(status, error.message, "error");
  }
}

// ---------- Logo ----------

function loadLogo() {
  const source = `/api/logo?v=${Date.now()}`;  // new URL so the browser skips its cache
  $("preview-logo").src = source;
  $("settings-logo").src = source;
}

function setHasLogo(hasLogo) {
  state.hasLogo = hasLogo;
  $("settings-logo").hidden = !hasLogo;
  $("no-logo").hidden = hasLogo;
  $("remove-logo").hidden = !hasLogo;
  showLogoInPreview();
}

function showLogoInPreview() {
  $("preview-logo").hidden = !(state.hasLogo && state.layout?.logo);
}

async function uploadLogo(event) {
  const file = event.target.files[0];
  if (!file) return;

  showStatus($("logo-status"), "Uploading…");
  try {
    await api("/api/logo", { method: "PUT", body: file });
    showStatus($("logo-status"), "Logo updated.", "ok");
    loadLogo();
  } catch (error) {
    showStatus($("logo-status"), error.message, "error");
  }
  event.target.value = "";
}

async function removeLogo() {
  try {
    await api("/api/logo", { method: "DELETE" });
    showStatus($("logo-status"), "Logo removed.", "ok");
    loadLogo();
  } catch (error) {
    showStatus($("logo-status"), error.message, "error");
  }
}

// ---------- Wiring ----------

$("title").addEventListener("input", updatePreview);
$("contents").addEventListener("input", updatePreview);

// Enter in the title moves to the contents instead of printing.
$("title").addEventListener("keydown", event => {
  if (event.key === "Enter") {
    event.preventDefault();
    $("contents").focus();
  }
});

// Ctrl + Enter (Cmd + Enter on a Mac) prints from anywhere.
document.addEventListener("keydown", event => {
  if (event.key === "Enter" && (event.ctrlKey || event.metaKey) && !$("settings").open) {
    event.preventDefault();
    printLabel();
  }
});

$("print").addEventListener("click", printLabel);
$("printer-status").addEventListener("click", openSettings);
$("open-settings").addEventListener("click", openSettings);
$("close-settings").addEventListener("click", () => $("settings").close());
$("settings-form").addEventListener("submit", saveSettings);

$("test-printer").addEventListener("click", () =>
  sendPrinterCommand("/api/printer/check", "test-status", "Connected to the printer."));
$("calibrate").addEventListener("click", () =>
  sendPrinterCommand("/api/printer/calibrate", "calibrate-status", "Calibrating. The printer will feed a few blank labels."));

$("logo-file").addEventListener("change", uploadLogo);
$("remove-logo").addEventListener("click", removeLogo);
$("preview-logo").addEventListener("load", () => setHasLogo(true));
$("preview-logo").addEventListener("error", () => setHasLogo(false));

async function init() {
  try {
    const [layouts, settings] = await Promise.all([api("/api/layouts"), api("/api/settings")]);
    state.layouts = layouts;
    state.printerIp = settings.printerIp;

    renderSizePicker();
    selectLayout(rememberedSize());
    loadLogo();
    checkPrinter();
    api("/api/version").then(info => { $("app-version").textContent = `Version ${info.version}`; }).catch(() => { });

    // Re-measure once the web font arrives, since it changes how text wraps.
    document.fonts.ready.then(updatePreview);
  } catch (error) {
    showStatus($("status"), error.message, "error");
  }
}

init();
