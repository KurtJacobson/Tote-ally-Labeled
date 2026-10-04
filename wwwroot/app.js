const $ = id => document.getElementById(id);

const state = {
  layouts: [],
  layout: null,
  hasLogo: false,
  printerIp: "",
  dpi: 203,
  icons: [],      // built-in icons, loaded the first time the icon picker opens
  myIcons: [],    // icons you uploaded
  icon: null      // the chosen icon
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
    throw new Error("Can't reach the Tote-ally Labeled app. Make sure it's still running.");
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

// Names come from the user, so they are always set as text, never as HTML.
function shapeIcon(size) {
  const shape = document.createElement("span");
  shape.className = "shape";
  shape.classList.toggle("round", size.round);
  shape.style.aspectRatio = `${size.widthInches} / ${size.round ? size.widthInches : size.heightInches}`;
  return shape;
}

function renderSizePicker() {
  $("size").replaceChildren(...state.layouts.map(layout => new Option(layout.name, layout.id)));
}

function selectLayout(id) {
  state.layout = state.layouts.find(layout => layout.id === id) ?? state.layouts[0];
  $("size").value = state.layout.id;

  try { localStorage.setItem("toteLabels.size", state.layout.id); } catch { }

  const contents = state.layout.contents;
  $("contents").disabled = !contents;
  $("contents-hint").textContent = contents
    ? `One item per line, up to ${contents.maxLines} lines`
    : "This size only has room for a title.";
  $("size-caption").textContent = `${state.layout.name} label${state.layout.sideways ? ", printed sideways" : ""}`;

  arrangePreview();
  updatePreview();
}

function rememberedSize() {
  try { return localStorage.getItem("toteLabels.size"); } catch { return null; }
}

// The layouts depend on the logo's proportions, so they are fetched again when the logo changes.
async function refreshLayouts() {
  applyLayouts(await api("/api/layouts"));
}

// After the sizes change: rebuild the picker and keep the current size if it still exists.
function applyLayouts(layouts) {
  state.layouts = layouts;
  renderSizePicker();
  selectLayout(state.layout?.id);
}

// ---------- Preview ----------
// The layout comes from the server in printer dots, so the preview is placed
// with the same numbers the printer uses, as percentages of the label.
// The main preview and the size editor's preview are both drawn by these.

// The main preview narrows with the window; the editor's preview is at most 300 px wide, which its panel
// always has room for, and leaving it uncapped is what lets the panel grow to the label's height.
const mainView = {
  fluid: true,
  icon: $("preview-icon"),
  label: $("label"), logo: $("preview-logo"), title: $("preview-title"),
  divider: $("preview-divider"), contents: $("preview-contents")
};

const sizeView = {
  label: $("size-label"), logo: $("size-preview-logo"), title: $("size-preview-title"),
  divider: $("size-preview-divider"), contents: $("size-preview-contents")
};

const percent = (dots, total) => `${(dots / total) * 100}%`;

function place(element, box, layout) {
  element.hidden = !box;
  if (!box) return;

  Object.assign(element.style, {
    left: percent(box.x, layout.widthDots),
    top: percent(box.y, layout.heightDots),
    width: percent(box.width, layout.widthDots),
    height: percent(box.height, layout.heightDots)
  });
}

function placeText(element, block, layout) {
  if (!block) {
    place(element, null, layout);
    return;
  }

  const lineHeight = block.fontSize + block.lineGap;
  place(element, { x: block.x, y: block.y, width: block.width, height: lineHeight * block.maxLines }, layout);

  element.style.fontSize = `${(block.fontSize / layout.widthDots) * 100}cqw`;
  element.style.lineHeight = lineHeight / block.fontSize;
  element.style.textAlign = block.align === "C" ? "center" : "left";
}

// Fits the label in a maxWidth × maxHeight px area, keeping its real proportions.
function arrangeLabel(view, layout, maxWidth, maxHeight) {
  const pixelsPerInch = Math.min(maxHeight / layout.heightInches, maxWidth / layout.widthInches);
  view.label.style.width = `${layout.widthInches * pixelsPerInch}px`;
  view.label.style.maxWidth = view.fluid ? "100%" : "";
  view.label.style.aspectRatio = `${layout.widthDots} / ${layout.heightDots}`;
  view.label.classList.toggle("round", layout.round);

  // With an icon chosen, the main preview moves the title over beside it, as the printer will.
  const withIcon = view.icon && state.icon;
  placeText(view.title, withIcon ? layout.titleBesideIcon : layout.title, layout);
  placeText(view.contents, layout.contents, layout);
  if (view.icon) {
    place(view.icon, withIcon && layout.icon, layout);
    view.icon.innerHTML = withIcon ? iconArt(state.icon) : "";
  }

  const rule = layout.divider;
  place(view.divider, rule && { x: rule.x, y: rule.y, width: rule.width, height: rule.thickness }, layout);

  place(view.logo, layout.logo, layout);
  view.logo.hidden = !(state.hasLogo && layout.logo);
}

function fillLabel(view, title, contents) {
  setPreviewText(view.title, title, sample.title);
  setPreviewText(view.contents, contents, sample.contents);
}

function setPreviewText(element, text, placeholder) {
  element.textContent = text || placeholder;
  element.classList.toggle("empty", !text);
}

// Which parts have more text than their block holds. Allow half a line of slack
// so rounding doesn't trigger a false warning.
function overflowingParts(view) {
  const overflows = element =>
    !element.hidden &&
    !element.classList.contains("empty") &&
    element.scrollHeight - element.clientHeight > parseFloat(getComputedStyle(element).fontSize) / 2;

  return [["title", view.title], ["contents", view.contents]]
    .filter(([, element]) => overflows(element))
    .map(([name]) => name);
}

function arrangePreview() {
  arrangeLabel(mainView, state.layout, 380, 400);
}

function updatePreview() {
  fillLabel(mainView, $("title").value.trim(), state.layout.contents ? $("contents").value.trimEnd() : "");
  checkFit();
}

function checkFit() {
  const parts = overflowingParts(mainView);
  const warning = $("fit-warning");
  warning.hidden = parts.length === 0;
  warning.textContent =
    `The ${parts.join(" and ")} won't fit on a ${state.layout.name} label. ` +
    "Shorten it, or the printer will print lines on top of each other.";
}

// ---------- Label size editor ----------
// Edits are held here until Save changes, which replaces the whole list on the server.
// The preview asks the server for each draft's layout, so it is exactly what would print.

const editor = { sizes: [], index: 0, unit: "in", dirty: false, timer: 0, request: 0 };

const MM_PER_INCH = 25.4;

function formatLength(inches, unit) {
  return unit === "mm"
    ? String(Math.round(inches * MM_PER_INCH * 10) / 10)
    : String(Math.round(inches * 1000) / 1000);
}

// Matches LabelLayouts.DisplayName on the server.
function sizeName(size) {
  if (size.name.trim()) return size.name.trim();
  const width = formatLength(size.widthInches, size.unit);
  return size.round
    ? `${width} ${size.unit} round`
    : `${width} × ${formatLength(size.heightInches, size.unit)} ${size.unit}`;
}

function openSizeEditor() {
  editor.sizes = state.layouts.map(layout => ({ ...layout.size }));
  editor.dirty = false;
  showStatus($("size-status"), "");
  selectSize(Math.max(0, editor.sizes.findIndex(size => size.id === state.layout.id)));
  $("size-editor").showModal();
}

function closeSizeEditor() {
  if (editor.dirty && !confirm("Discard the changes to label sizes?")) return;
  $("size-editor").close();
}

function renderSizeList() {
  const list = $("size-list");
  list.replaceChildren();
  editor.sizes.forEach((size, index) => {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "size-item";
    button.setAttribute("aria-current", index === editor.index ? "true" : "false");
    button.append(shapeIcon(size), sizeName(size));
    button.addEventListener("click", () => selectSize(index));
    const item = document.createElement("li");
    item.append(button);
    list.append(item);
  });
  $("remove-size").disabled = editor.sizes.length < 2;
}

function selectSize(index) {
  editor.index = index;
  fillSizeForm(editor.sizes[index]);
  renderSizeList();
  previewSize();
}

function checkRadio(name, value) {
  document.querySelector(`input[name="${name}"][value="${value}"]`).checked = true;
}

const radioValue = name => document.querySelector(`input[name="${name}"]:checked`).value;

function fillSizeForm(size) {
  editor.unit = size.unit;
  checkRadio("shape", size.round ? "round" : "rect");
  checkRadio("unit", size.unit);
  $("size-width").value = formatLength(size.widthInches, size.unit);
  $("size-height").value = formatLength(size.heightInches, size.unit);
  $("size-name").value = size.name;
  $("size-logo").checked = size.showLogo;
  checkRadio("title-lines", size.titleLines);
  checkRadio("align", size.contentsAlign);
  showShapeFields(size);
}

function showShapeFields(size) {
  const widest = size.unit === "mm" ? "103.9 mm" : "4.09 in";
  $("height-field").hidden = size.round;
  $("size-times").hidden = size.round;
  $("width-label").textContent = size.round ? "Diameter" : "Width";
  $("size-name").placeholder = sizeName({ ...size, name: "" });
  $("size-hint").textContent = size.round
    ? `Round labels can be up to ${widest} across.`
    : `One side can be up to ${widest}, the widest the printer prints. ` +
      "A label wider than that prints sideways on a narrower roll.";
}

function readSizeForm() {
  const unit = radioValue("unit");
  const toInches = value => parseFloat(value) / (unit === "mm" ? MM_PER_INCH : 1);
  const round = radioValue("shape") === "round";
  const widthInches = toInches($("size-width").value);
  return {
    ...editor.sizes[editor.index],
    name: $("size-name").value,
    widthInches,
    heightInches: round ? widthInches : toInches($("size-height").value),
    unit,
    round,
    showLogo: $("size-logo").checked,
    titleLines: Number(radioValue("title-lines")),
    contentsAlign: radioValue("align")
  };
}

function onSizeFormInput(event) {
  // Switching units keeps the size and rewrites the numbers in the new unit.
  // Left-aligned contents look lopsided in a circle, so choosing Round centres them (they can be changed back).
  if (event.target.name === "shape" && event.target.value === "round") checkRadio("align", "C");

  if (event.target.name === "unit") {
    const before = editor.sizes[editor.index];
    editor.unit = event.target.value;
    $("size-width").value = formatLength(before.widthInches, editor.unit);
    $("size-height").value = formatLength(before.heightInches, editor.unit);
  }

  const size = readSizeForm();
  editor.sizes[editor.index] = size;
  editor.dirty = true;
  showShapeFields(size);
  renderSizeList();
  showStatus($("size-status"), "");
  clearTimeout(editor.timer);
  editor.timer = setTimeout(previewSize, 150);
}

async function previewSize() {
  const size = editor.sizes[editor.index];
  const request = ++editor.request;

  if (!(size.widthInches > 0) || !(size.heightInches > 0)) {
    showSizeProblem(size.round ? "Enter the diameter." : "Enter the width and height.");
    return;
  }

  try {
    const layout = await sendJson("/api/sizes/preview", size);
    if (request !== editor.request) return;  // a newer edit is already on its way

    sizeView.label.classList.remove("invalid");
    arrangeLabel(sizeView, layout, 300, 280);
    fillLabel(sizeView, "", "");
    $("size-preview-caption").textContent =
      (layout.contents ? `Up to ${layout.contents.maxLines} content lines` : "Room for a title only") +
      (layout.sideways ? ", printed sideways" : "");

    const logoHint = !size.showLogo ? ""
      : !state.hasLogo ? "No logo has been uploaded yet. Upload one in Settings."
      : !layout.logo ? "There is no room for the logo inside this circle with the title and contents."
      : "";
    $("logo-hint").hidden = !logoHint;
    $("logo-hint").textContent = logoHint;
  } catch (error) {
    if (request === editor.request) showSizeProblem(error.message);
  }
}

function showSizeProblem(message) {
  sizeView.label.classList.add("invalid");
  showStatus($("size-status"), message, "error");
}

function addSize() {
  editor.sizes.push({
    id: "", name: "", widthInches: 4, heightInches: 2, unit: editor.unit,
    round: false, showLogo: false, titleLines: 1, contentsAlign: "L"
  });
  editor.dirty = true;
  selectSize(editor.sizes.length - 1);
  $("size-width").focus();
}

function removeSize() {
  if (editor.sizes.length < 2) return;
  editor.sizes.splice(editor.index, 1);
  editor.dirty = true;
  selectSize(Math.min(editor.index, editor.sizes.length - 1));
}

async function saveSizes(event) {
  event.preventDefault();
  $("save-size").disabled = true;
  try {
    const layouts = await sendJson("/api/sizes", editor.sizes, "PUT");
    applyLayouts(layouts);
    editor.sizes = layouts.map(layout => ({ ...layout.size }));
    editor.dirty = false;
    selectSize(Math.min(editor.index, editor.sizes.length - 1));
    showStatus($("size-status"), "Saved.", "ok");
  } catch (error) {
    showStatus($("size-status"), error.message, "error");
  } finally {
    $("save-size").disabled = false;
  }
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
      copies,
      iconPng: state.icon ? await renderIconPng() : null
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

  for (const id of ["test-status", "logo-status", "my-icons-status", "calibrate-status", "save-status"]) {
    showStatus($(id), "");
  }
  showMyIconsInSettings();
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
    state.dpi = dpi;
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
  $("size-preview-logo").src = source;
}

function setHasLogo(hasLogo) {
  state.hasLogo = hasLogo;
  $("settings-logo").hidden = !hasLogo;
  $("no-logo").hidden = hasLogo;
  $("remove-logo").hidden = !hasLogo;
  showLogoInPreview();
}

function showLogoInPreview() {
  if (state.layout) arrangePreview();
}

async function uploadLogo(event) {
  const file = event.target.files[0];
  if (!file) return;

  showStatus($("logo-status"), "Uploading…");
  try {
    await api("/api/logo", { method: "PUT", body: file });
    showStatus($("logo-status"), "Logo updated.", "ok");
    loadLogo();
    await refreshLayouts();
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
    await refreshLayouts();
  } catch (error) {
    showStatus($("logo-status"), error.message, "error");
  }
}

// ---------- Icons ----------
// icons.json holds the built-in icons: label, category, search words and SVG shapes.
// Your own icons are PNGs stored by the app and listed by /api/icons.

const MY_ICONS = "My icons";
const MAX_RECENT = 16;

function iconArt(icon) {
  return icon.src
    ? `<img src="${icon.src}" alt="">`
    : `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${icon.viewBox}" fill="currentColor">${icon.body}</svg>`;
}

function addSearchWords(icon) {
  icon.searchWords = `${icon.label} ${icon.tags} ${icon.category}`.toLowerCase().split(/\s+/);
  return icon;
}

async function loadIcons() {
  if (state.icons.length) return;

  $("results-note").textContent = "Loading icons…";
  state.icons = (await api("/icons.json")).map(addSearchWords);

  const categories = [...new Set(state.icons.map(icon => icon.category)), MY_ICONS];
  for (const category of categories) {
    $("icon-category").add(new Option(category, category));
  }
}

async function loadMyIcons() {
  const names = (await api("/api/icons")) ?? [];
  const version = Date.now();  // new URLs so a replaced icon isn't shown from the browser's cache
  state.myIcons = names.map(name => addSearchWords({
    id: `my:${name}`,
    label: name,
    category: MY_ICONS,
    tags: "",
    src: `/api/icons/${encodeURIComponent(name)}?v=${version}`
  }));
}

const allIcons = () => [...state.icons, ...state.myIcons];

async function openIconPicker() {
  $("icon-picker").showModal();
  try {
    await Promise.all([loadIcons(), loadMyIcons()]);
    showIcons();
  } catch (error) {
    $("results-note").textContent = error.message;
  }
}

function currentMatches() {
  const words = $("icon-search").value.toLowerCase().split(/\s+/).filter(Boolean);
  const category = $("icon-category").value;

  // Each search word must be the start of one of the icon's words, so "jar" finds
  // "mason jars" but not "pajamas".
  const matchesWord = (iconWords, word) => iconWords.some(iconWord => iconWord.startsWith(word));

  const matches = allIcons().filter(icon =>
    (!category || icon.category === category) &&
    words.every(word => matchesWord(icon.searchWords, word)));

  // Icons whose name matches the search come before ones that only match a search word.
  const nameHits = icon => words.filter(word => matchesWord(icon.label.toLowerCase().split(/\s+/), word)).length;
  return words.length ? matches.sort((a, b) => nameHits(b) - nameHits(a)) : matches;
}

// With no search or category, the picker shows every icon grouped by category.
function showIcons() {
  const browsing = !$("icon-search").value.trim() && !$("icon-category").value;
  const sections = [];

  if (browsing) {
    const recent = recentIcons();
    if (recent.length) sections.push(iconSection("Recently used", recent));

    const groups = Map.groupBy(allIcons(), icon => icon.category);
    for (const [category, icons] of groups) {
      sections.push(iconSection(category, icons));
    }
  } else {
    const matches = currentMatches();
    if (matches.length) sections.push(iconSection(`Results (${matches.length})`, matches));
  }

  $("icon-results").replaceChildren(...sections);
  $("results-note").textContent = sections.length ? "" :
    $("icon-category").value === MY_ICONS && !state.myIcons.length
      ? "You haven't uploaded any icons yet. Use Upload icon below to add one."
      : "No icons match. Try another word, or add your own: upload one, or copy an image or SVG and press Ctrl+V.";
}

function iconSection(heading, icons) {
  const section = document.createElement("section");
  const title = document.createElement("h3");
  title.className = "picker-heading";
  title.textContent = heading;
  const grid = document.createElement("div");
  grid.className = "icon-grid";
  grid.append(...icons.map(iconChoice));
  section.append(title, grid);
  return section;
}

function iconChoice(icon) {
  const button = document.createElement("button");
  button.type = "button";
  button.className = "icon-choice";
  button.title = icon.label;
  button.setAttribute("aria-pressed", state.icon?.id === icon.id);
  button.innerHTML = `
    <span class="icon-art" aria-hidden="true">${iconArt(icon)}</span>
    <span class="icon-name"></span>`;
  button.querySelector(".icon-name").textContent = icon.label;
  button.addEventListener("click", () => chooseIcon(icon));
  return button;
}

function chooseIcon(icon) {
  state.icon = icon;
  if (icon) rememberIcon(icon);

  $("icon-picker").close();
  updateIconButton();
  arrangePreview();
  updatePreview();
  $("title").focus();
}

function updateIconButton() {
  const button = $("icon-button");
  button.classList.toggle("has-icon", !!state.icon);
  $("icon-button-art").innerHTML = state.icon ? iconArt(state.icon) : "";
  $("icon-button-text").textContent = state.icon ? "" : "Icon";
  button.setAttribute("aria-label", state.icon ? `Icon: ${state.icon.label}. Change icon` : "Add an icon");
}

// Recently used icons are remembered per browser.
function recentIcons() {
  try {
    const ids = JSON.parse(localStorage.getItem("toteLabels.recentIcons")) ?? [];
    const icons = allIcons();
    return ids.map(id => icons.find(icon => icon.id === id)).filter(Boolean);
  } catch {
    return [];
  }
}

function rememberIcon(icon) {
  try {
    const ids = (JSON.parse(localStorage.getItem("toteLabels.recentIcons")) ?? []).filter(id => id !== icon.id);
    ids.unshift(icon.id);
    localStorage.setItem("toteLabels.recentIcons", JSON.stringify(ids.slice(0, MAX_RECENT)));
  } catch { }
}

// Draws an image file onto a canvas of the given size, scaled to fit and centered.
async function drawImage(source, width, height, background) {
  const image = new Image();
  image.src = source;
  await image.decode();

  // SVG files without a size report 0 × 0, so fall back to a square.
  const naturalWidth = image.naturalWidth || 512;
  const naturalHeight = image.naturalHeight || 512;
  const scale = Math.min(width / naturalWidth, height / naturalHeight);
  const drawWidth = naturalWidth * scale;
  const drawHeight = naturalHeight * scale;

  const canvas = document.createElement("canvas");
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext("2d");
  if (background) {
    context.fillStyle = background;
    context.fillRect(0, 0, width, height);
  }
  context.drawImage(image, (width - drawWidth) / 2, (height - drawHeight) / 2, drawWidth, drawHeight);
  return canvas;
}

// Draws the chosen icon at its exact printed size, in black on white, for the printer.
async function renderIconPng() {
  const box = state.layout.icon;
  const width = Math.round(box.width * state.dpi / 203);
  const height = Math.round(box.height * state.dpi / 203);

  const source = state.icon.src ??
    "data:image/svg+xml;charset=utf-8," + encodeURIComponent(iconArt(state.icon).replaceAll("currentColor", "#000"));

  const canvas = await drawImage(source, width, height, "#fff");
  return canvas.toDataURL("image/png");
}

// ---------- Your own icons ----------

// Uploads an image as one of your icons and returns it, or shows why it failed and returns null.
async function uploadIcon(file, note) {
  showStatus(note, "Uploading…");

  // Convert to a 256 px PNG here, so the app accepts SVGs too and stores small files.
  const fileUrl = URL.createObjectURL(file);
  let png;
  try {
    const canvas = await drawImage(fileUrl, 256, 256, null);
    png = await new Promise(resolve => canvas.toBlob(resolve, "image/png"));
  } catch {
    showStatus(note, "That file couldn't be read as an image. Try a PNG, JPG or SVG.", "error");
    return null;
  } finally {
    URL.revokeObjectURL(fileUrl);
  }

  try {
    const requestedName = file.name.replace(/\.[^.]+$/, "").replaceAll(/[-_]+/g, " ");
    const { name } = await api(`/api/icons?name=${encodeURIComponent(requestedName)}`, { method: "PUT", body: png });
    await loadMyIcons();
    showStatus(note, `Added ${name}.`, "ok");
    return state.myIcons.find(icon => icon.label === name);
  } catch (error) {
    showStatus(note, error.message, "error");
    return null;
  }
}

async function showMyIconsInSettings() {
  try {
    await loadMyIcons();
  } catch (error) {
    showStatus($("my-icons-status"), error.message, "error");
    return;
  }

  const list = $("my-icons-list");
  list.replaceChildren(...state.myIcons.map(icon => {
    const row = document.createElement("li");
    row.innerHTML = `<span class="icon-art" aria-hidden="true">${iconArt(icon)}</span><span class="my-icon-name"></span>`;
    row.querySelector(".my-icon-name").textContent = icon.label;

    const remove = document.createElement("button");
    remove.type = "button";
    remove.className = "button-quiet";
    remove.textContent = "Remove";
    remove.setAttribute("aria-label", `Remove ${icon.label}`);
    remove.addEventListener("click", () => removeMyIcon(icon));
    row.append(remove);
    return row;
  }));

  $("no-my-icons").hidden = state.myIcons.length > 0;
}

async function removeMyIcon(icon) {
  try {
    await api(`/api/icons/${encodeURIComponent(icon.label)}`, { method: "DELETE" });
    showStatus($("my-icons-status"), `Removed ${icon.label}.`, "ok");

    if (state.icon?.id === icon.id) {
      state.icon = null;
      updateIconButton();
      arrangePreview();
      updatePreview();
    }
    showMyIconsInSettings();
  } catch (error) {
    showStatus($("my-icons-status"), error.message, "error");
  }
}

// ---------- Pasting icons ----------
// With the picker or Settings open, Ctrl+V adds the icon on the clipboard: an image (copied from a web page,
// or a screenshot) or SVG code (an icon site's "Copy SVG"). Any other text pastes as normal.

function pastedIcon(clipboard) {
  const image = [...clipboard.items].find(item => item.kind === "file" && item.type.startsWith("image/"));
  if (image) return image.getAsFile();

  let text = clipboard.getData("text/plain").trim();
  if (!text.startsWith("<") || !/<svg[\s>]/i.test(text) || !/<\/svg>$/i.test(text)) return null;

  // An SVG only draws as an image with its namespace, which copied code sometimes leaves out.
  if (!/<svg[^>]*\sxmlns=/i.test(text)) text = text.replace(/<svg/i, '<svg xmlns="http://www.w3.org/2000/svg"');
  return new Blob([text], { type: "image/svg+xml" });
}

document.addEventListener("paste", async event => {
  const inPicker = $("icon-picker").open;
  if (!inPicker && !$("settings").open) return;

  const icon = pastedIcon(event.clipboardData);
  if (!icon) return;
  event.preventDefault();

  // A pasted icon has no file name, so it is named after the search, which is usually what it was looked for as.
  const name = (inPicker && $("icon-search").value.trim()) || "Pasted icon";
  const file = new File([icon], `${name}.png`, { type: icon.type });

  if (inPicker) {
    const added = await uploadIcon(file, $("results-note"));
    if (added) chooseIcon(added);
  } else if (await uploadIcon(file, $("my-icons-status"))) {
    showMyIconsInSettings();
  }
});

// ---------- Wiring ----------

$("size").addEventListener("change", event => selectLayout(event.target.value));
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
  if (event.key === "Enter" && (event.ctrlKey || event.metaKey) && !document.querySelector("dialog[open]")) {
    event.preventDefault();
    printLabel();
  }
});

$("print").addEventListener("click", printLabel);
$("printer-status").addEventListener("click", openSettings);
$("open-settings").addEventListener("click", openSettings);
$("close-settings").addEventListener("click", () => $("settings").close());
$("settings-form").addEventListener("submit", saveSettings);

$("icon-button").addEventListener("click", openIconPicker);
$("close-picker").addEventListener("click", () => $("icon-picker").close());
$("remove-icon").addEventListener("click", () => chooseIcon(null));
$("icon-search").addEventListener("input", showIcons);
$("icon-category").addEventListener("change", showIcons);
// Uploading from the picker also chooses the new icon; uploading from Settings just adds it.
$("icon-upload").addEventListener("change", async event => {
  const file = event.target.files[0];
  event.target.value = "";
  const icon = file && await uploadIcon(file, $("results-note"));
  if (icon) chooseIcon(icon);
});
$("settings-icon-upload").addEventListener("change", async event => {
  const file = event.target.files[0];
  event.target.value = "";
  if (file && await uploadIcon(file, $("my-icons-status"))) showMyIconsInSettings();
});

// Enter in the search box picks the first result.
$("icon-search").addEventListener("keydown", event => {
  if (event.key === "Enter") {
    event.preventDefault();
    $("icon-results").querySelector(".icon-choice")?.click();
  }
});

$("edit-sizes").addEventListener("click", openSizeEditor);
$("close-size-editor").addEventListener("click", closeSizeEditor);
$("size-editor").addEventListener("cancel", event => {  // Esc
  event.preventDefault();
  closeSizeEditor();
});
$("size-form").addEventListener("input", onSizeFormInput);
$("size-form").addEventListener("submit", saveSizes);
$("add-size").addEventListener("click", addSize);
$("remove-size").addEventListener("click", removeSize);

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
    state.dpi = settings.dpi;

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
