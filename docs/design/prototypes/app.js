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
function openModal(id) {
  var el = document.getElementById(id);
  if (!el) return;
  el.removeAttribute("hidden");
  var focusable = el.querySelector("input, button, [tabindex]");
  if (focusable) focusable.focus();
}
function closeModal(id) {
  var el = document.getElementById(id);
  if (el) el.setAttribute("hidden", "");
}
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
      m.setAttribute("hidden", "");
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
function showToast(kind, message, actionLabel, actionFn) {
  var stack = document.querySelector(".toast-stack");
  if (!stack) return;
  var el = document.createElement("div");
  el.className = "toast " + kind;
  var icon = kind === "success" ? "✓" : kind === "danger" ? "⚠" : "ℹ";
  el.innerHTML =
    '<span class="ic">' + icon + '</span><span class="msg">' + message + '</span>';
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
function cycleConnectionDemo() {
  var banner = document.getElementById("conn-banner");
  if (!banner) return;
  var state = banner.dataset.state || "connected";
  var next = { connected: "reconnecting", reconnecting: "failed", failed: "connected" }[state];
  banner.dataset.state = next;
  banner.className = "conn-banner";
  if (next === "reconnecting") {
    banner.classList.add("show", "warning");
    banner.innerHTML = '<span class="spin"></span> Reconnecting… changes made in the last few seconds may not be saved.';
    freezeInputs(true);
  } else if (next === "failed") {
    banner.classList.add("show", "danger");
    banner.innerHTML = 'Connection lost. Editing is disabled until the page is reloaded. <button class="link" onclick="cycleConnectionDemo()">Reload page (demo: click to reset)</button>';
    freezeInputs(true);
  } else {
    banner.classList.remove("show");
    freezeInputs(false);
  }
}
function freezeInputs(frozen) {
  document.querySelectorAll(".line-editor .cell-input, .info-grid .input").forEach(function (el) {
    el.disabled = frozen;
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
