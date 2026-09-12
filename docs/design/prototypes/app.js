/*
 * app.js — shared, framework-free behavior for the static prototypes.
 * Everything here is a stand-in for what the real Blazor Server app does
 * server-side; comments mark exactly where the real implementation differs.
 */

// ---------------------------------------------------------------------------
// Theme: explicit choice wins, else prefers-color-scheme, matching tokens.css.
// ---------------------------------------------------------------------------
(function themeInit() {
  var stored = null;
  try { stored = localStorage.getItem("aurora-theme"); } catch (e) { /* ignore */ }
  if (stored === "light" || stored === "dark") {
    document.documentElement.setAttribute("data-theme", stored);
  }
})();

function setTheme(mode) {
  if (mode === "system") {
    document.documentElement.removeAttribute("data-theme");
    try { localStorage.removeItem("aurora-theme"); } catch (e) {}
  } else {
    document.documentElement.setAttribute("data-theme", mode);
    try { localStorage.setItem("aurora-theme", mode); } catch (e) {}
  }
  syncThemeToggle();
}

function syncThemeToggle() {
  var stored = null;
  try { stored = localStorage.getItem("aurora-theme"); } catch (e) {}
  document.querySelectorAll(".theme-toggle button").forEach(function (btn) {
    btn.classList.toggle("active", btn.dataset.mode === (stored || "system"));
  });
}
document.addEventListener("DOMContentLoaded", syncThemeToggle);

// ---------------------------------------------------------------------------
// Nav rail collapse
// ---------------------------------------------------------------------------
function toggleRail() {
  var shell = document.querySelector(".shell");
  if (shell) shell.classList.toggle("rail-collapsed");
}

// ---------------------------------------------------------------------------
// Generic popover open/close (company switcher, notifications, user menu)
// ---------------------------------------------------------------------------
function togglePopover(id, anchorEvent) {
  if (anchorEvent) anchorEvent.stopPropagation();
  document.querySelectorAll(".popover").forEach(function (p) {
    if (p.id !== id) p.setAttribute("hidden", "");
  });
  var el = document.getElementById(id);
  if (el) el.toggleAttribute("hidden");
}
document.addEventListener("click", function () {
  document.querySelectorAll(".popover").forEach(function (p) { p.setAttribute("hidden", ""); });
});

// ---------------------------------------------------------------------------
// Company switch confirmation demo.
// Real app: blocks only when there is unsaved work; logs the switch
// (who/when/from/to) to the audit log; re-scopes the whole shell.
// ---------------------------------------------------------------------------
function requestCompanySwitch(name, hasUnsaved) {
  document.querySelectorAll(".popover").forEach(function (p) { p.setAttribute("hidden", ""); });
  if (hasUnsaved) {
    openModal("switch-confirm-modal");
    document.getElementById("switch-target-name").textContent = name;
  } else {
    showToast("success", "Switched to " + name + ".");
  }
}

// ---------------------------------------------------------------------------
// Modal / dialog helpers
// ---------------------------------------------------------------------------
// DESIGN-001 added the focus trap and the focus restore below. components.md
// §14 requires both ("Focus is trapped inside the dialog (Tab cycles within
// it)" and "Closing: focus returns to the element that opened the dialog"),
// and until now no prototype implemented either: focus tabbed straight out of
// an open dialog into the page behind the scrim (WCAG 2.2 SC 2.4.3 Focus
// Order, SC 4.1.2 Name/Role/Value), and closing a dialog dropped focus onto
// <body> (SC 2.4.3). The re-authentication dialog in sign-in.html and the
// company-switch dialog in shell.html both inherit the fix.
var MODAL_FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]),' +
  ' textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

function modalFocusables(el) {
  return Array.prototype.filter.call(el.querySelectorAll(MODAL_FOCUSABLE), function (n) {
    return !n.hasAttribute("hidden") && (n.offsetWidth > 0 || n.offsetHeight > 0 || n === document.activeElement);
  });
}

function openModal(id) {
  var el = document.getElementById(id);
  if (!el) return;
  el.auroraOpener = document.activeElement;
  el.removeAttribute("hidden");
  var first = el.querySelector("[data-autofocus]") || modalFocusables(el)[0];
  if (first) first.focus();
}
function closeModal(id) {
  var el = document.getElementById(id);
  if (!el) return;
  el.setAttribute("hidden", "");
  var opener = el.auroraOpener;
  el.auroraOpener = null;
  if (opener && document.contains(opener) && typeof opener.focus === "function") opener.focus();
}

// Tab / Shift+Tab cycle within the topmost open overlay, never out of it.
document.addEventListener("keydown", function (e) {
  if (e.key !== "Tab") return;
  var overlays = document.querySelectorAll(".modal-overlay:not([hidden])");
  if (!overlays.length) return;
  var open = overlays[overlays.length - 1];
  var items = modalFocusables(open);
  if (!items.length) return;
  var first = items[0];
  var last = items[items.length - 1];
  var inside = open.contains(document.activeElement);
  if (e.shiftKey && (!inside || document.activeElement === first)) {
    e.preventDefault();
    last.focus();
  } else if (!e.shiftKey && (!inside || document.activeElement === last)) {
    e.preventDefault();
    first.focus();
  }
});

document.addEventListener("keydown", function (e) {
  if (e.key === "Escape") {
    // components.md #14: "Esc closes it UNLESS it represents an in-progress,
    // already-committed action... a dialog can be dismissed while merely
    // collecting input, never while a commit is actually in flight." A
    // forced re-authentication dialog (see sign-in.html) is the same class
    // of case — closing it would leave the user silently unauthenticated —
    // so it opts out via data-modal-blocking="true" rather than being
    // silently dismissable like the command palette or the company-switch
    // confirmation.
    document.querySelectorAll(".modal-overlay:not([hidden])").forEach(function (m) {
      if (m.dataset.modalBlocking === "true") return;
      closeModal(m.id); // via closeModal, so Esc restores focus to the opener too
    });
  }
  // Command palette shortcut
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
    e.preventDefault();
    var cmdk = document.getElementById("cmdk-modal");
    if (cmdk) {
      cmdk.hasAttribute("hidden") ? openModal("cmdk-modal") : closeModal("cmdk-modal");
    }
  }
});

// ---------------------------------------------------------------------------
// Toasts
// ---------------------------------------------------------------------------
// DESIGN-001 added announce()/ensureLiveRegion(). components.md §15 already
// required it ("a screen-reader-only live region announces the message
// regardless of the visual toast") but no prototype had one, so every toast
// in every prototype was a visual-only notification — WCAG 2.2 SC 4.1.3
// Status Messages. Polite for success/info; assertive for an error, which
// interrupts because an unread error is the one that costs the user work.
function ensureLiveRegion(assertive) {
  var id = assertive ? "aurora-live-assertive" : "aurora-live-polite";
  var el = document.getElementById(id);
  if (!el) {
    el = document.createElement("div");
    el.id = id;
    el.className = "visually-hidden";
    el.setAttribute("role", assertive ? "alert" : "status");
    el.setAttribute("aria-live", assertive ? "assertive" : "polite");
    el.setAttribute("aria-atomic", "true");
    document.body.appendChild(el);
  }
  return el;
}
function announce(message, assertive) {
  var el = ensureLiveRegion(!!assertive);
  el.textContent = "";
  window.setTimeout(function () { el.textContent = message; }, 30);
}

function showToast(kind, message, actionLabel, actionFn) {
  var stack = document.querySelector(".toast-stack");
  if (!stack) return;
  var el = document.createElement("div");
  el.className = "toast " + kind;
  var icon = kind === "success" ? "✓" : kind === "danger" ? "⚠" : "ℹ";
  // textContent, not innerHTML: a toast message can carry user-entered data
  // (a company name), and the prototype must not model string concatenation
  // into markup as the normal way to do this.
  var ic = document.createElement("span");
  ic.className = "ic";
  ic.setAttribute("aria-hidden", "true");
  ic.textContent = icon;
  var msg = document.createElement("span");
  msg.className = "msg";
  msg.textContent = message;
  el.appendChild(ic);
  el.appendChild(msg);
  announce(message, kind === "danger");
  if (actionLabel) {
    var btn = document.createElement("button");
    btn.className = "action";
    btn.textContent = actionLabel;
    btn.onclick = function () { if (actionFn) actionFn(); el.remove(); };
    el.appendChild(btn);
  }
  stack.appendChild(el);
  var autoDismiss = kind !== "danger" && !actionLabel;
  if (autoDismiss) {
    setTimeout(function () { el.remove(); }, 5000);
  }
}

// ---------------------------------------------------------------------------
// Connection banner demo — cycles through the states specified in
// app-shell.md. In the real app this reflects Blazor Server's actual
// SignalR circuit state; here it's a manual cycle for visual review.
// ---------------------------------------------------------------------------
// setConnectionState was extracted from cycleConnectionDemo by DESIGN-001 so a
// screen can drive one specific transition (e.g. "the circuit drops mid-submit
// and then comes back") instead of only cycling. Its three strings went
// through t() at the same time: they were the last hard-coded user-facing
// English left in the prototypes, which CLAUDE.md forbids outright.
function setConnectionState(state) {
  var banner = document.getElementById("conn-banner");
  if (!banner) return;
  banner.dataset.state = state;
  banner.className = "conn-banner";
  if (state === "reconnecting") {
    banner.classList.add("show", "warning");
    banner.textContent = "";
    var spin = document.createElement("span");
    spin.className = "spin";
    spin.setAttribute("aria-hidden", "true");
    banner.appendChild(spin);
    banner.appendChild(document.createTextNode(" " + t("shell.reconnecting")));
    freezeInputs(true);
  } else if (state === "failed") {
    banner.classList.add("show", "danger");
    banner.textContent = t("shell.connectionLost") + " ";
    var reload = document.createElement("button");
    reload.className = "link";
    reload.textContent = t("shell.reloadPage");
    reload.onclick = function () { setConnectionState("connected"); };
    banner.appendChild(reload);
    freezeInputs(true);
  } else {
    banner.classList.remove("show");
    banner.textContent = "";
    freezeInputs(false);
  }
}

function cycleConnectionDemo() {
  var banner = document.getElementById("conn-banner");
  if (!banner) return;
  var state = banner.dataset.state || "connected";
  setConnectionState({ connected: "reconnecting", reconnecting: "failed", failed: "connected" }[state]);
}
// app-shell.md's reconnection rule ("all editable controls in the content area
// become disabled... the user's in-progress data stays visible, just frozen")
// is implemented with readOnly + aria-disabled rather than the native `disabled`
// attribute, for the same reason components.md §1 made that correction for
// buttons: `disabled` on the element that currently HAS focus drops focus to
// <body> (WCAG 2.2 SC 2.4.3), and a circuit drop is precisely the moment a user
// is mid-keystroke in a field. readOnly keeps focus, the caret and the value.
// The selector covers the create dialog too, so the claim holds on a screen
// whose only editable control lives in a dialog.
function freezeInputs(frozen) {
  document.querySelectorAll(".line-editor .cell-input, .info-grid .input, .dialog .input").forEach(function (el) {
    el.readOnly = frozen;
    if (frozen) { el.setAttribute("aria-disabled", "true"); } else { el.removeAttribute("aria-disabled"); }
  });
}

// ---------------------------------------------------------------------------
// Grid: client-side sort demo over a fixed fake dataset.
// Real app: sorting re-queries the server for the current page — see
// components.md #8. This simulates only the visual/interaction result.
// ---------------------------------------------------------------------------
function sortGridBy(table, colIndex, type) {
  var tbody = table.querySelector("tbody");
  var rows = Array.from(tbody.querySelectorAll("tr"));
  var th = table.querySelectorAll("thead th")[colIndex];
  var dir = th.dataset.dir === "asc" ? "desc" : "asc";
  table.querySelectorAll("thead th").forEach(function (h) { h.dataset.dir = ""; h.querySelector(".arrow") && (h.querySelector(".arrow").textContent = ""); });
  th.dataset.dir = dir;
  var arrow = th.querySelector(".arrow");
  if (arrow) arrow.textContent = dir === "asc" ? "↑" : "↓";

  rows.sort(function (a, b) {
    var av = a.children[colIndex].dataset.sort || a.children[colIndex].textContent.trim();
    var bv = b.children[colIndex].dataset.sort || b.children[colIndex].textContent.trim();
    if (type === "num") { av = parseFloat(av) || 0; bv = parseFloat(bv) || 0; return dir === "asc" ? av - bv : bv - av; }
    return dir === "asc" ? String(av).localeCompare(bv) : String(bv).localeCompare(av);
  });
  rows.forEach(function (r) { tbody.appendChild(r); });
}

// ---------------------------------------------------------------------------
// Grid: row selection -> bulk action bar
// ---------------------------------------------------------------------------
function onRowCheck() {
  var boxes = document.querySelectorAll(".grid-body-checkbox");
  var checked = Array.from(boxes).filter(function (b) { return b.checked; });
  var bar = document.getElementById("bulk-bar");
  var countEl = document.getElementById("bulk-count");
  if (!bar) return;
  bar.classList.toggle("show", checked.length > 0);
  if (countEl) countEl.textContent = checked.length;
  checked.forEach(function (b) { b.closest("tr").classList.add("selected"); });
  Array.from(boxes).filter(function (b) { return !b.checked; }).forEach(function (b) {
    b.closest("tr").classList.remove("selected");
  });
  var headCb = document.getElementById("grid-select-all");
  if (headCb) {
    headCb.checked = checked.length === boxes.length && boxes.length > 0;
    headCb.indeterminate = checked.length > 0 && checked.length < boxes.length;
  }
}
function toggleAllRows(checkbox) {
  document.querySelectorAll(".grid-body-checkbox").forEach(function (b) { b.checked = checkbox.checked; });
  onRowCheck();
}
function clearSelection() {
  document.querySelectorAll(".grid-body-checkbox").forEach(function (b) { b.checked = false; });
  onRowCheck();
}

// ---------------------------------------------------------------------------
// Filter chip removal (visual demo only)
// ---------------------------------------------------------------------------
function removeChip(el) {
  el.closest(".chip").remove();
}

// ---------------------------------------------------------------------------
// Document line editor: keyboard model (Tab/Shift+Tab across, Enter commits
// and drops to the same column on the next line — spreadsheet convention),
// running totals, and inline tax-override flag. See components.md #11.
// ---------------------------------------------------------------------------
function lineEditorKeydown(e, input) {
  if (e.key !== "Enter") return;
  e.preventDefault();
  var row = input.closest("tr");
  var colIndex = Array.from(row.children).indexOf(input.closest("td"));
  var wasBlankLine = row.classList.contains("new-line");

  // Committing the trailing blank line promotes it to a real line and a
  // fresh blank line is appended below it — the spreadsheet convention from
  // components.md #11 ("Enter on the last line creates a new one").
  if (wasBlankLine) row.classList.remove("new-line");
  var nextRow = row.nextElementSibling;
  if (!nextRow) {
    addLine();
    nextRow = row.parentElement.lastElementChild;
  }

  var targetCell = nextRow.children[colIndex];
  var targetInput = targetCell ? targetCell.querySelector(".cell-input") : null;
  if (targetInput) {
    targetInput.focus();
    if (targetInput.select) targetInput.select();
  }
}

var lineSeq = 100;
function addLine() {
  var tbody = document.getElementById("line-body");
  if (!tbody) return;
  lineSeq++;
  var tr = document.createElement("tr");
  tr.className = "new-line";
  tr.innerHTML =
    '<td><input class="cell-input" list="item-options" placeholder="Type to find item…" onkeydown="lineEditorKeydown(event,this)" oninput="this.closest(\'tr\').classList.remove(\'new-line\')"></td>' +
    '<td><input class="cell-input" placeholder="Description" onkeydown="lineEditorKeydown(event,this)"></td>' +
    '<td><input class="cell-input num tnum" type="text" value="1" onkeydown="lineEditorKeydown(event,this)" oninput="recalcLine(this)"></td>' +
    '<td><input class="cell-input num tnum" type="text" value="0.00" onkeydown="lineEditorKeydown(event,this)" oninput="recalcLine(this)"></td>' +
    '<td><input class="cell-input num tnum" type="text" value="0" onkeydown="lineEditorKeydown(event,this)" oninput="recalcLine(this)"></td>' +
    '<td class="tax-cell"><select class="cell-input" onkeydown="lineEditorKeydown(event,this)" onchange="recalcLine(this)"><option>Standard 20%</option><option>Reduced 5%</option><option>Zero-rated 0%</option></select></td>' +
    '<td class="num tnum line-total">$0.00</td>' +
    '<td><button class="row-del" title="Remove line" onclick="this.closest(\'tr\').remove(); recalcTotals();">✕</button></td>';
  tbody.appendChild(tr);
  recalcTotals();
}

function recalcLine(el) {
  var row = el.closest("tr");
  row.classList.remove("new-line");
  var qty = parseFloat(row.children[2].querySelector("input").value) || 0;
  var price = parseFloat(row.children[3].querySelector("input").value) || 0;
  var disc = parseFloat(row.children[4].querySelector("input").value) || 0;
  var total = qty * price * (1 - disc / 100);
  row.querySelector(".line-total").textContent = formatMoney(total);
  recalcTotals();
}

function formatMoney(n) {
  // Illustrative only: en-US grouping/decimal. Production formats per the
  // tenant's active locale/Country Package, never a hard-coded format — see
  // tokens.md Principle 6 and components.md #5.
  return "$" + n.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function recalcTotals() {
  var rows = document.querySelectorAll("#line-body tr:not(.new-line)");
  var subtotal = 0, taxTotal = 0;
  var taxBuckets = {};
  rows.forEach(function (row) {
    var qty = parseFloat(row.children[2].querySelector("input").value) || 0;
    var price = parseFloat(row.children[3].querySelector("input").value) || 0;
    var disc = parseFloat(row.children[4].querySelector("input").value) || 0;
    var lineNet = qty * price * (1 - disc / 100);
    subtotal += lineNet;
    var taxSel = row.children[5].querySelector("select");
    var rate = taxSel && taxSel.value.indexOf("20%") >= 0 ? 0.20 : taxSel && taxSel.value.indexOf("5%") >= 0 ? 0.05 : 0;
    var lineTax = lineNet * rate;
    taxTotal += lineTax;
    var key = taxSel ? taxSel.value : "Standard 20%";
    taxBuckets[key] = (taxBuckets[key] || 0) + lineTax;
  });
  var subtotalEl = document.getElementById("t-subtotal");
  var grandEl = document.getElementById("t-grand");
  var breakdownEl = document.getElementById("tax-breakdown");
  if (subtotalEl) subtotalEl.textContent = formatMoney(subtotal);
  if (grandEl) grandEl.textContent = formatMoney(subtotal + taxTotal);
  // Tax breakdown by rate/code, per components.md #11 — one row per code in
  // use, not a single blended figure, so a reviewer can see exactly which
  // rates make up the total.
  if (breakdownEl) {
    var keys = Object.keys(taxBuckets).filter(function (k) { return taxBuckets[k] > 0.004; });
    breakdownEl.innerHTML = keys.length
      ? keys.map(function (k) {
          return '<tr><td class="label">Tax — ' + k + '</td><td class="val tnum">' + formatMoney(taxBuckets[k]) + '</td></tr>';
        }).join("")
      : '<tr><td class="label">Tax</td><td class="val tnum">' + formatMoney(0) + '</td></tr>';
  }
}

document.addEventListener("DOMContentLoaded", function () {
  if (document.getElementById("line-body")) recalcTotals();
});

// ---------------------------------------------------------------------------
// Bulk paste: pasting tab/newline-delimited rows (e.g. copied from a
// customer's PO spreadsheet) creates multiple lines in one action.
// See components.md #11 "Bulk paste".
// ---------------------------------------------------------------------------
function handleLinePaste(e, input) {
  var text = (e.clipboardData || window.clipboardData).getData("text");
  if (!text || (text.indexOf("\n") === -1 && text.indexOf("\t") === -1)) return; // single value: normal paste
  e.preventDefault();
  var lines = text.split(/\r?\n/).filter(function (l) { return l.trim().length > 0; });
  var currentRow = input.closest("tr");
  lines.forEach(function (line, i) {
    var cells = line.split("\t");
    var targetRow = i === 0 ? currentRow : document.getElementById("line-body").lastElementChild;
    if (i > 0) { addLine(); targetRow = document.getElementById("line-body").lastElementChild; }
    targetRow.classList.remove("new-line");
    var itemInput = targetRow.children[0].querySelector("input");
    var descInput = targetRow.children[1].querySelector("input");
    var qtyInput = targetRow.children[2].querySelector("input");
    var priceInput = targetRow.children[3].querySelector("input");
    if (itemInput) itemInput.value = cells[0] || "";
    if (descInput && cells[1]) descInput.value = cells[1];
    if (qtyInput && cells[2]) qtyInput.value = cells[2];
    if (priceInput && cells[3]) priceInput.value = cells[3];
    recalcLine(qtyInput || itemInput);
  });
  showToast("success", "Pasted " + lines.length + " line" + (lines.length > 1 ? "s" : "") + " from clipboard.");
}

// ---------------------------------------------------------------------------
// Posted / read-only document demo toggle (components.md #11).
// ---------------------------------------------------------------------------
function togglePostedDemo() {
  var doc = document.getElementById("doc-root");
  var posted = doc.classList.toggle("is-posted");
  document.querySelectorAll(".line-editor .cell-input, .line-editor select, .line-editor .row-del, #add-line-btn").forEach(function (el) {
    el.disabled = posted;
    el.style.visibility = (posted && el.classList.contains("row-del")) ? "hidden" : "";
  });
  var statusBadge = document.getElementById("doc-status-badge");
  var confirmBtn = document.getElementById("confirm-btn");
  if (posted) {
    statusBadge.className = "badge badge-success";
    statusBadge.textContent = "Posted";
    confirmBtn.disabled = true;
    showToast("success", "Document posted. Lines are now read-only — corrections require a reversal.");
  } else {
    statusBadge.className = "badge badge-info";
    statusBadge.textContent = "Draft";
    confirmBtn.disabled = false;
  }
}
