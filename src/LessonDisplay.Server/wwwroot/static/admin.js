(function () {
  "use strict";

  var state = { data: null, courseKey: null, selectedIndex: null };

  var courseSelect = document.getElementById("courseSelect");
  var lessonList = document.getElementById("lessonList");
  var editCard = document.getElementById("editCard");
  var editTitle = document.getElementById("editTitle");
  var topicInput = document.getElementById("topicInput");
  var liInput = document.getElementById("liInput");
  var scInput = document.getElementById("scInput");
  var makeCurrentInput = document.getElementById("makeCurrentInput");
  var lessonStatus = document.getElementById("lessonStatus");
  var scheduleBody = document.getElementById("scheduleBody");
  var scheduleStatus = document.getElementById("scheduleStatus");
  var courseStatus = document.getElementById("courseStatus");

  // ---------------- tabs ----------------
  document.querySelectorAll(".tab-btn").forEach(function (btn) {
    btn.addEventListener("click", function () {
      document.querySelectorAll(".tab-btn").forEach(function (b) { b.classList.remove("active"); });
      document.querySelectorAll(".tab-panel").forEach(function (p) { p.style.display = "none"; });
      btn.classList.add("active");
      document.getElementById("tab-" + btn.dataset.tab).style.display = "block";
      if (btn.dataset.tab === "schedule") renderSchedule();
      if (btn.dataset.tab === "links") renderLinks();
      if (btn.dataset.tab === "style") renderStyleTab();
    });
  });

  function renderLinks() {
    var origin = window.location.origin;
    document.getElementById("liLink").textContent = origin + "/display/learning-intention";
    document.getElementById("scLink").textContent = origin + "/display/success-criteria";

    fetch("/api/server-info", { cache: "no-store" })
      .then(function (r) { return r.json(); })
      .then(function (info) {
        document.getElementById("serverAddr").textContent = info.admin_url;
        document.getElementById("serverAlias").textContent =
          "It may also work as http://" + info.hostname_alias + ":" + info.port + "/admin " +
          "on networks that support it — the address above always works.";
      })
      .catch(function () {
        document.getElementById("serverAddr").textContent = origin + "/admin";
      });
  }

  var restartMiniPcBtn = document.getElementById("restartMiniPcBtn");
  var restartStatus = document.getElementById("restartStatus");
  if (restartMiniPcBtn) {
    restartMiniPcBtn.addEventListener("click", function () {
      if (!confirm("Restart this PC now? Both displays will go dark for a minute or two while it restarts.")) return;
      restartMiniPcBtn.disabled = true;
      fetch("/api/restart/trigger", { method: "POST" })
        .then(function (r) { if (!r.ok) throw new Error("failed"); return r.json(); })
        .then(function () {
          showStatus(restartStatus, "Restarting now — the PC will be back within about a minute or two.", true);
        })
        .catch(function () {
          showStatus(restartStatus, "Could not send the restart command — check the server is reachable.", false);
        })
        .finally(function () {
          restartMiniPcBtn.disabled = false;
        });
    });
  }

  // ---------------- data loading ----------------
  function loadData(afterLoad) {
    fetch("/api/data", { cache: "no-store" })
      .then(function (r) { return r.json(); })
      .then(function (json) {
        state.data = json;
        if (!state.courseKey || !json.courses[state.courseKey]) {
          state.courseKey = Object.keys(json.courses)[0] || null;
        }
        populateCourseSelect();
        renderLessonList();
        if (afterLoad) afterLoad();
      });
  }

  function populateCourseSelect() {
    courseSelect.innerHTML = "";
    Object.keys(state.data.courses).forEach(function (key) {
      var opt = document.createElement("option");
      opt.value = key;
      opt.textContent = state.data.courses[key].name;
      if (key === state.courseKey) opt.selected = true;
      courseSelect.appendChild(opt);
    });
  }

  courseSelect.addEventListener("change", function () {
    state.courseKey = courseSelect.value;
    state.selectedIndex = null;
    editCard.style.display = "none";
    renderLessonList();
  });

  // ---------------- lesson list ----------------
  function renderLessonList() {
    lessonList.innerHTML = "";
    var course = state.data.courses[state.courseKey];
    if (!course) return;
    course.lessons.forEach(function (lesson, idx) {
      var row = document.createElement("div");
      var isCurrent = idx === course.current_index;
      row.className = "lesson-row" + (isCurrent ? "" : " not-current") + (idx === state.selectedIndex ? " selected" : "");
      row.innerHTML =
        '<span class="badge">' + (isCurrent ? "CURRENT" : "   ") + "</span>" +
        '<span class="title">Lesson ' + lesson.lesson_number + (lesson.topic ? ": " + escapeHtml(lesson.topic) : "") + "</span>";
      row.addEventListener("click", function () { selectLesson(idx); });
      lessonList.appendChild(row);
    });
  }

  function escapeHtml(s) {
    return String(s).replace(/[&<>"']/g, function (c) {
      return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
    });
  }

  function selectLesson(idx) {
    state.selectedIndex = idx;
    var course = state.data.courses[state.courseKey];
    var lesson = course.lessons[idx];
    editTitle.textContent = "Edit Lesson " + lesson.lesson_number;
    topicInput.value = lesson.topic || "";
    liInput.value = lesson.learning_intention || "";
    scInput.value = (lesson.success_criteria || []).join("\n");
    makeCurrentInput.checked = idx === course.current_index;
    lessonStatus.className = "status-msg";
    editCard.style.display = "block";
    renderLessonList();
  }

  document.getElementById("addLessonBtn").addEventListener("click", function () {
    state.selectedIndex = null;
    editTitle.textContent = "New Lesson";
    topicInput.value = "";
    liInput.value = "";
    scInput.value = "";
    makeCurrentInput.checked = true;
    lessonStatus.className = "status-msg";
    editCard.style.display = "block";
    topicInput.focus();
  });

  document.getElementById("cancelEditBtn").addEventListener("click", function () {
    editCard.style.display = "none";
    state.selectedIndex = null;
    renderLessonList();
  });

  document.getElementById("saveLessonBtn").addEventListener("click", function () {
    var payload = {
      course_key: state.courseKey,
      index: state.selectedIndex,
      topic: topicInput.value,
      learning_intention: liInput.value,
      success_criteria: scInput.value,
      make_current: makeCurrentInput.checked,
    };
    fetch("/api/lesson/save", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    })
      .then(function (r) { if (!r.ok) throw new Error("save failed"); return r.json(); })
      .then(function () {
        showStatus(lessonStatus, "Saved.", true);
        loadData(function () {
          selectLesson(state.selectedIndex === null ? state.data.courses[state.courseKey].lessons.length - 1 : state.selectedIndex);
        });
      })
      .catch(function () { showStatus(lessonStatus, "Could not save — check the server is reachable.", false); });
  });

  document.getElementById("setCurrentBtn").addEventListener("click", function () {
    if (state.selectedIndex === null) return;
    fetch("/api/lesson/current", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ course_key: state.courseKey, index: state.selectedIndex }),
    })
      .then(function (r) { if (!r.ok) throw new Error("failed"); return r.json(); })
      .then(function () {
        showStatus(lessonStatus, "This is now the current lesson for " + state.data.courses[state.courseKey].name + ".", true);
        loadData(function () { selectLesson(state.selectedIndex); });
      })
      .catch(function () { showStatus(lessonStatus, "Could not update — check the server is reachable.", false); });
  });

  document.getElementById("deleteLessonBtn").addEventListener("click", function () {
    if (state.selectedIndex === null) { editCard.style.display = "none"; return; }
    if (!confirm("Delete this lesson? This can't be undone.")) return;
    fetch("/api/lesson/delete", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ course_key: state.courseKey, index: state.selectedIndex }),
    })
      .then(function (r) { if (!r.ok) throw new Error("failed"); return r.json(); })
      .then(function () {
        editCard.style.display = "none";
        state.selectedIndex = null;
        loadData();
      })
      .catch(function () { showStatus(lessonStatus, "Could not delete — check the server is reachable.", false); });
  });

  function showStatus(el, msg, ok) {
    el.textContent = msg;
    el.className = "status-msg " + (ok ? "ok" : "err");
  }

  // ---------------- schedule ----------------
  var scheduleSwitcher = document.getElementById("scheduleSwitcher");
  var activeScheduleStatus = document.getElementById("activeScheduleStatus");
  var scheduleEditSelect = document.getElementById("scheduleEditSelect");
  var newScheduleCopyFrom = document.getElementById("newScheduleCopyFrom");
  var newScheduleStatus = document.getElementById("newScheduleStatus");

  function scheduleLabel(key) {
    return (state.data.schedule_labels && state.data.schedule_labels[key]) || key;
  }

  function renderSchedule() {
    // 1) the big "which schedule is live" switcher
    scheduleSwitcher.innerHTML = "";
    Object.keys(state.data.schedules).forEach(function (key) {
      var isActive = key === state.data.active_schedule;
      var btn = document.createElement("button");
      btn.type = "button";
      btn.className = "btn " + (isActive ? "btn-success" : "btn-secondary");
      btn.textContent = isActive ? "✓ " + scheduleLabel(key) + " (live now)" : "Use " + scheduleLabel(key);
      btn.disabled = isActive;
      btn.addEventListener("click", function () {
        fetch("/api/schedule/active", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ key: key }),
        })
          .then(function (r) { if (!r.ok) throw new Error("failed"); return r.json(); })
          .then(function () {
            showStatus(activeScheduleStatus, "Displays are now using “" + scheduleLabel(key) + "”.", true);
            loadData(function () { renderSchedule(); });
          })
          .catch(function () { showStatus(activeScheduleStatus, "Could not switch schedule — check the server is reachable.", false); });
      });
      scheduleSwitcher.appendChild(btn);
    });

    // 2) the "which schedule am I editing" dropdown
    var previousEditKey = scheduleEditSelect.value;
    scheduleEditSelect.innerHTML = "";
    newScheduleCopyFrom.innerHTML = "";
    Object.keys(state.data.schedules).forEach(function (key) {
      var opt = document.createElement("option");
      opt.value = key;
      opt.textContent = scheduleLabel(key);
      scheduleEditSelect.appendChild(opt);

      var opt2 = document.createElement("option");
      opt2.value = key;
      opt2.textContent = scheduleLabel(key);
      newScheduleCopyFrom.appendChild(opt2);
    });
    var editKey = (previousEditKey && state.data.schedules[previousEditKey]) ? previousEditKey : state.data.active_schedule;
    scheduleEditSelect.value = editKey;

    renderScheduleRows(editKey);
  }

  function renderScheduleRows(scheduleKey) {
    scheduleBody.innerHTML = "";
    (state.data.schedules[scheduleKey] || []).forEach(function (p) { addScheduleRow(p); });
  }

  scheduleEditSelect.addEventListener("change", function () {
    renderScheduleRows(scheduleEditSelect.value);
  });

  function addScheduleRow(p) {
    p = p || { label: "", course: state.courseKey, start: "", end: "" };
    var tr = document.createElement("tr");

    var labelTd = document.createElement("td");
    var labelInput = document.createElement("input");
    labelInput.type = "text";
    labelInput.value = p.label || "";
    labelInput.className = "sched-label";
    labelTd.appendChild(labelInput);

    var courseTd = document.createElement("td");
    var courseSel = document.createElement("select");
    courseSel.className = "sched-course";
    Object.keys(state.data.courses).forEach(function (key) {
      var opt = document.createElement("option");
      opt.value = key;
      opt.textContent = state.data.courses[key].name;
      if (key === p.course) opt.selected = true;
      courseSel.appendChild(opt);
    });
    courseTd.appendChild(courseSel);

    var startTd = document.createElement("td");
    var startInput = document.createElement("input");
    startInput.type = "time";
    startInput.value = p.start || "";
    startInput.className = "sched-start";
    startTd.appendChild(startInput);

    var endTd = document.createElement("td");
    var endInput = document.createElement("input");
    endInput.type = "time";
    endInput.value = p.end || "";
    endInput.className = "sched-end";
    endTd.appendChild(endInput);

    var delTd = document.createElement("td");
    var delBtn = document.createElement("button");
    delBtn.type = "button";
    delBtn.textContent = "Remove";
    delBtn.className = "del-btn";
    delBtn.addEventListener("click", function () { tr.remove(); });
    delTd.appendChild(delBtn);

    tr.appendChild(labelTd);
    tr.appendChild(courseTd);
    tr.appendChild(startTd);
    tr.appendChild(endTd);
    tr.appendChild(delTd);
    scheduleBody.appendChild(tr);
  }

  document.getElementById("addPeriodBtn").addEventListener("click", function () { addScheduleRow(); });

  document.getElementById("saveScheduleBtn").addEventListener("click", function () {
    var scheduleKey = scheduleEditSelect.value;
    var rows = Array.prototype.slice.call(scheduleBody.querySelectorAll("tr"));
    var periods = rows.map(function (tr, i) {
      return {
        period: i + 1,
        label: tr.querySelector(".sched-label").value.trim(),
        course: tr.querySelector(".sched-course").value,
        start: tr.querySelector(".sched-start").value,
        end: tr.querySelector(".sched-end").value,
      };
    });
    fetch("/api/schedule/save", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name: scheduleKey, periods: periods }),
    })
      .then(function (r) { if (!r.ok) return r.text().then(function (t) { throw new Error(t); }); return r.json(); })
      .then(function () {
        showStatus(scheduleStatus, "“" + scheduleLabel(scheduleKey) + "” saved.", true);
        loadData();
      })
      .catch(function (e) { showStatus(scheduleStatus, "Could not save schedule — check every row has a label, course, start and end time.", false); });
  });

  document.getElementById("addScheduleBtn").addEventListener("click", function () {
    var name = document.getElementById("newScheduleName").value.trim();
    if (!name) return;
    var copyFrom = newScheduleCopyFrom.value;
    fetch("/api/schedule/create", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name: name, copy_from: copyFrom }),
    })
      .then(function (r) { if (!r.ok) throw new Error("failed"); return r.json(); })
      .then(function (res) {
        showStatus(newScheduleStatus, "Added “" + name + "”, copied from “" + scheduleLabel(copyFrom) + "”. Edit its rows above, or switch to it whenever it's needed.", true);
        document.getElementById("newScheduleName").value = "";
        loadData(function () {
          scheduleEditSelect.value = res.key;
          renderScheduleRows(res.key);
        });
      })
      .catch(function () { showStatus(newScheduleStatus, "Could not add schedule — check the server is reachable.", false); });
  });

  // ---------------- add course ----------------
  document.getElementById("addCourseBtn").addEventListener("click", function () {
    var name = document.getElementById("newCourseName").value.trim();
    if (!name) return;
    fetch("/api/course/save", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name: name }),
    })
      .then(function (r) { if (!r.ok) throw new Error("failed"); return r.json(); })
      .then(function (res) {
        showStatus(courseStatus, "Added \"" + name + "\". Switch to the Lessons tab to fill it in, or Bell Schedule to assign it a period.", true);
        document.getElementById("newCourseName").value = "";
        state.courseKey = res.key;
        loadData();
      })
      .catch(function () { showStatus(courseStatus, "Could not add course — check the server is reachable.", false); });
  });

  // ---------------- display style ----------------
  var STYLE_DEFAULTS = {
    li: { font_family: "", font_size: 96, bg_start: "#4338CA", bg_end: "#7C3AED", accent: "#FDE047" },
    sc: { font_family: "", font_size: 46, bg_start: "#047857", bg_end: "#10B981", accent: "#FCD34D" },
  };
  var loadedPreviewFonts = {}; // font name -> true, so we don't re-inject the same <link>

  function styleFields(screen) {
    return {
      font: document.getElementById(screen + "FontInput"),
      size: document.getElementById(screen + "FontSizeInput"),
      bgStart: document.getElementById(screen + "BgStartInput"),
      bgEnd: document.getElementById(screen + "BgEndInput"),
      accent: document.getElementById(screen + "AccentInput"),
      preview: document.getElementById(screen + "Preview"),
      previewText: document.getElementById(screen + "PreviewText"),
      status: document.getElementById(screen + "StyleStatus"),
      saveBtn: document.getElementById("save" + (screen === "li" ? "Li" : "Sc") + "StyleBtn"),
    };
  }

  function ensurePreviewFont(family) {
    if (!family || loadedPreviewFonts[family]) return;
    loadedPreviewFonts[family] = true;
    var link = document.createElement("link");
    link.rel = "stylesheet";
    link.href = "https://fonts.googleapis.com/css2?family=" +
      encodeURIComponent(family).replace(/%20/g, "+") + ":wght@700;800&display=swap";
    document.head.appendChild(link);
  }

  function updatePreview(screen) {
    var f = styleFields(screen);
    var family = f.font.value.trim();
    if (family) ensurePreviewFont(family);
    f.preview.style.background = "linear-gradient(160deg, " + f.bgStart.value + " 0%, " + f.bgEnd.value + " 100%)";
    f.preview.querySelector(".preview-label").style.color = f.accent.value;
    f.previewText.style.color = f.accent.value;
    f.previewText.style.fontFamily = family ? '"' + family + '", sans-serif' : "";
  }

  function fillStyleForm(screen) {
    var f = styleFields(screen);
    var s = (state.data.display_styles && state.data.display_styles[screen]) || STYLE_DEFAULTS[screen];
    f.font.value = s.font_family || "";
    f.size.value = s.font_size || STYLE_DEFAULTS[screen].font_size;
    f.bgStart.value = s.bg_start || STYLE_DEFAULTS[screen].bg_start;
    f.bgEnd.value = s.bg_end || STYLE_DEFAULTS[screen].bg_end;
    f.accent.value = s.accent || STYLE_DEFAULTS[screen].accent;
    f.status.className = "status-msg";
    updatePreview(screen);
  }

  function wireStyleForm(screen) {
    var f = styleFields(screen);
    [f.font, f.bgStart, f.bgEnd, f.accent].forEach(function (el) {
      el.addEventListener("input", function () { updatePreview(screen); });
    });
    f.saveBtn.addEventListener("click", function () {
      var payload = {
        screen: screen,
        font_family: f.font.value.trim(),
        font_size: parseInt(f.size.value, 10) || STYLE_DEFAULTS[screen].font_size,
        bg_start: f.bgStart.value,
        bg_end: f.bgEnd.value,
        accent: f.accent.value,
      };
      fetch("/api/style/save", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      })
        .then(function (r) { if (!r.ok) return r.text().then(function (t) { throw new Error(t); }); return r.json(); })
        .then(function () {
          showStatus(f.status, "Saved — the display will pick this up within ~20 seconds.", true);
          loadData();
        })
        .catch(function () { showStatus(f.status, "Could not save — check the server is reachable.", false); });
    });
  }

  function renderStyleTab() {
    fillStyleForm("li");
    fillStyleForm("sc");
  }

  wireStyleForm("li");
  wireStyleForm("sc");

  loadData();
})();
