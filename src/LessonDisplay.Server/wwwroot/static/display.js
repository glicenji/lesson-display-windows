(function () {
  "use strict";

  // No server-side templating on the static-file version of this page — both
  // /display/learning-intention and /display/success-criteria serve the same
  // physical file, so the mode is read from the URL instead of an injected var.
  var MODE = window.location.pathname.indexOf("success-criteria") !== -1 ? "sc" : "li";
  var bodyEl = document.getElementById("body");
  var screenEl = document.getElementById("screen");

  var latestData = null;
  var lastRenderKey = null;
  var everLoaded = false;
  var lastFontFamily = null; // tracks which Google Font <link> is currently injected

  var DEFAULT_STYLE = MODE === "li"
    ? { font_family: "", font_size: 96, bg_start: "#4338CA", bg_end: "#7C3AED", accent: "#FDE047" }
    : { font_family: "", font_size: 46, bg_start: "#047857", bg_end: "#10B981", accent: "#FCD34D" };

  function currentStyle() {
    var styles = latestData && latestData.display_styles;
    return (styles && styles[MODE]) || DEFAULT_STYLE;
  }

  function applyStyle(style) {
    var root = document.documentElement.style;
    root.setProperty("--bg-start", style.bg_start || DEFAULT_STYLE.bg_start);
    root.setProperty("--bg-end", style.bg_end || DEFAULT_STYLE.bg_end);
    root.setProperty("--accent", style.accent || DEFAULT_STYLE.accent);

    var family = (style.font_family || "").trim();
    if (family) {
      root.setProperty("--display-font", '"' + family + '", var(--font)');
      if (family !== lastFontFamily) {
        lastFontFamily = family;
        var linkId = "google-font-link";
        var existing = document.getElementById(linkId);
        if (existing) existing.remove();
        var link = document.createElement("link");
        link.id = linkId;
        link.rel = "stylesheet";
        link.href = "https://fonts.googleapis.com/css2?family=" +
          encodeURIComponent(family).replace(/%20/g, "+") + ":wght@700;800&display=swap";
        document.head.appendChild(link);
      }
    } else {
      root.setProperty("--display-font", "var(--font)");
      lastFontFamily = null;
      var stale = document.getElementById("google-font-link");
      if (stale) stale.remove();
    }
  }

  // ---- optional ?preview=HH:MM to preview what a given time looks like ----
  var previewParam = new URLSearchParams(window.location.search).get("preview");

  function nowSeconds() {
    var d = new Date();
    if (previewParam && /^\d{1,2}:\d{2}$/.test(previewParam)) {
      var parts = previewParam.split(":");
      return parseInt(parts[0], 10) * 3600 + parseInt(parts[1], 10) * 60 + d.getSeconds();
    }
    return d.getHours() * 3600 + d.getMinutes() * 60 + d.getSeconds();
  }

  function toSeconds(hhmm) {
    var parts = hhmm.split(":");
    return parseInt(parts[0], 10) * 3600 + parseInt(parts[1], 10) * 60;
  }

  function findActivePeriod(periods) {
    var s = nowSeconds();
    for (var i = 0; i < periods.length; i++) {
      var p = periods[i];
      var start = toSeconds(p.start);
      var end = toSeconds(p.end);
      if (s >= start && s < end) return p;
    }
    return null;
  }

  function formatCountdown(totalSeconds) {
    if (totalSeconds < 0) totalSeconds = 0;
    var m = Math.floor(totalSeconds / 60);
    var s = totalSeconds % 60;
    return (m < 10 ? "0" : "") + m + ":" + (s < 10 ? "0" : "") + s;
  }

  var URGENT_THRESHOLD_SECONDS = 3 * 60;

  function updateCountdown(period) {
    var el = document.getElementById("countdown");
    if (!el || !period) return;
    var remaining = toSeconds(period.end) - nowSeconds();
    el.textContent = formatCountdown(remaining);
    if (remaining <= URGENT_THRESHOLD_SECONDS) {
      el.classList.add("urgent");
    } else {
      el.classList.remove("urgent");
    }
  }

  function fetchData() {
    fetch("/api/data", { cache: "no-store" })
      .then(function (res) {
        if (!res.ok) throw new Error("bad status " + res.status);
        return res.json();
      })
      .then(function (json) {
        latestData = json;
        everLoaded = true;
        applyStyle(currentStyle());
        tick();
      })
      .catch(function () {
        if (!everLoaded) renderConnecting();
        // if we already have data, keep showing it silently and retry later
      });
  }

  function renderConnecting() {
    var key = "connecting";
    if (key === lastRenderKey) return;
    lastRenderKey = key;
    bodyEl.className = "display theme-connecting";
    screenEl.innerHTML = '<div class="connecting-wrap">Connecting to lesson server&hellip;</div>';
  }

  function escapeHtml(s) {
    return String(s).replace(/[&<>"']/g, function (c) {
      return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
    });
  }

  function renderGoodbye() {
    var key = "goodbye";
    if (key === lastRenderKey) return;
    lastRenderKey = key;
    bodyEl.className = "display theme-bye";
    screenEl.innerHTML =
      '<div class="goodbye-wrap">' +
      '  <div class="goodbye-emoji">👋</div>' +
      '  <div class="goodbye-text">Have a Nice Day!</div>' +
      "</div>";
  }

  function renderClass(period, course, lesson) {
    var key = "class:" + period.period + ":" + course.name + ":" + course.current_index + ":" + MODE;
    var themeClass = MODE === "li" ? "theme-li" : "theme-sc";
    var icon = MODE === "li" ? "💡" : "✅";
    var title = MODE === "li" ? "LEARNING INTENTION" : "SUCCESS CRITERIA";

    var header = period.label + " &bull; " + escapeHtml(course.name);
    if (lesson.topic) header += ": " + escapeHtml(lesson.topic);

    var contentHtml;
    if (MODE === "li") {
      var text = lesson.learning_intention && lesson.learning_intention.trim()
        ? lesson.learning_intention
        : "(no learning intention set yet — add one in the admin page)";
      contentHtml = '<div class="statement" id="fitEl">' + escapeHtml(text) + "</div>";
    } else {
      var items = (lesson.success_criteria && lesson.success_criteria.length)
        ? lesson.success_criteria
        : ["(no success criteria set yet — add some in the admin page)"];
      var lis = items.map(function (it) {
        return '<li><span class="check">&#10003;</span><span>' + escapeHtml(it) + "</span></li>";
      }).join("");
      contentHtml = '<ul class="criteria-list" id="fitEl">' + lis + "</ul>";
    }

    if (key === lastRenderKey) return; // avoid re-render/flicker every tick
    lastRenderKey = key;

    bodyEl.className = "display " + themeClass;
    screenEl.innerHTML =
      '<div class="topbar"><span>' + header + '</span>' +
      '<span class="right-group"><span class="countdown-badge" id="countdown">--:--</span>' +
      '<span class="icon">' + icon + "</span></span></div>" +
      '<div class="accent-bar"></div>' +
      '<h1 class="section-title">' + title + "</h1>" +
      '<div class="content-wrap" id="contentWrap">' + contentHtml + "</div>" +
      '<div class="footer">Updates automatically each period</div>';

    fitText();
    updateCountdown(period);
  }

  function fitText() {
    var wrap = document.getElementById("contentWrap");
    var el = document.getElementById("fitEl");
    if (!wrap || !el) return;
    var style = currentStyle();
    var max = style.font_size || DEFAULT_STYLE.font_size;
    var min = 20;
    var size = max;
    el.style.fontSize = size + "px";
    // allow layout to settle, then shrink until it fits
    var guard = 0;
    while (el.scrollHeight > wrap.clientHeight && size > min && guard < 60) {
      size -= 2;
      el.style.fontSize = size + "px";
      guard++;
    }
  }

  function tick() {
    if (!latestData) {
      renderConnecting();
      return;
    }
    var activePeriods = (latestData.schedules && latestData.schedules[latestData.active_schedule]) || [];
    var period = findActivePeriod(activePeriods);
    if (!period) {
      renderGoodbye();
      return;
    }
    var course = latestData.courses[period.course];
    if (!course || !course.lessons || !course.lessons.length) {
      renderGoodbye();
      return;
    }
    var idx = Math.min(course.current_index || 0, course.lessons.length - 1);
    var lesson = course.lessons[idx];
    renderClass(period, course, lesson);
    updateCountdown(period); // also refresh every tick even when renderClass skipped its own re-render
  }

  window.addEventListener("resize", function () {
    lastRenderKey = null; // force re-render so text re-fits new dimensions
    tick();
  });

  fetchData();
  setInterval(fetchData, 20000);
  setInterval(tick, 1000);
})();
