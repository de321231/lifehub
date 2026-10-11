/* =========================================================================
   DESD LifeHub – Oberflächenlogik
   Progressive Enhancement: alle Funktionen haben eine serverseitige Entsprechung.
   ========================================================================= */
(function () {
    "use strict";

    var csrfToken = function () {
        var meta = document.querySelector('meta[name="csrf-token"]');
        return meta ? meta.getAttribute("content") : "";
    };

    /* ------------------------------------------------------------ Theme */
    function currentTheme() {
        var explicit = document.documentElement.getAttribute("data-theme");
        if (explicit === "light" || explicit === "dark") {
            return explicit;
        }
        return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
    }

    function applyTheme(theme) {
        if (theme === "system") {
            document.documentElement.removeAttribute("data-theme");
            try { localStorage.removeItem("desd-lifehub-theme"); } catch (e) { /* ignorieren */ }
        } else {
            document.documentElement.setAttribute("data-theme", theme);
            try { localStorage.setItem("desd-lifehub-theme", theme); } catch (e) { /* ignorieren */ }
        }

        document.querySelectorAll("[data-theme-label]").forEach(function (el) {
            el.textContent = theme === "dark" ? "Dunkel" : theme === "light" ? "Hell" : "System";
        });
    }

    document.querySelectorAll("[data-theme-toggle]").forEach(function (button) {
        button.addEventListener("click", function () {
            var next = currentTheme() === "dark" ? "light" : "dark";
            applyTheme(next);

            // Einstellung serverseitig speichern (nicht kritisch für die Bedienung).
            fetch("/api/settings/theme", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "X-CSRF-TOKEN": csrfToken()
                },
                body: JSON.stringify({ theme: next })
            }).catch(function () { /* offline: lokale Einstellung bleibt aktiv */ });
        });
    });

    /* ------------------------------------------------------------ Sidebar */
    var sidebar = document.querySelector("[data-sidebar]");
    var backdrop = document.querySelector("[data-backdrop]");

    function setSidebar(open) {
        if (!sidebar) return;
        sidebar.classList.toggle("open", open);
        if (backdrop) backdrop.classList.toggle("open", open);
        var toggle = document.querySelector("[data-sidebar-toggle]");
        if (toggle) toggle.setAttribute("aria-expanded", open ? "true" : "false");
    }

    document.querySelectorAll("[data-sidebar-toggle]").forEach(function (button) {
        button.addEventListener("click", function () {
            setSidebar(!sidebar.classList.contains("open"));
        });
    });

    if (backdrop) backdrop.addEventListener("click", function () { setSidebar(false); });

    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape") setSidebar(false);
    });

    /* ------------------------------------------------------------ Bestätigungen */
    document.querySelectorAll("form[data-confirm]").forEach(function (form) {
        form.addEventListener("submit", function (event) {
            if (!window.confirm(form.getAttribute("data-confirm"))) {
                event.preventDefault();
            }
        });
    });

    /* ------------------------------------------------------------ Auto-Submit von Filtern */
    document.querySelectorAll("[data-autosubmit]").forEach(function (control) {
        control.addEventListener("change", function () {
            var form = control.closest("form");
            if (form) form.submit();
        });
    });

    /* ------------------------------------------------------------ Meldungen schließen */
    document.querySelectorAll("[data-dismiss]").forEach(function (button) {
        button.addEventListener("click", function () {
            var target = button.closest(".alert, .toast");
            if (target) target.remove();
        });
    });

    setTimeout(function () {
        document.querySelectorAll(".toast").forEach(function (toast) { toast.remove(); });
    }, 7000);

    /* ------------------------------------------------------------ Board: Drag & Drop */
    var dragged = null;

    document.querySelectorAll("[data-task-card]").forEach(function (card) {
        card.setAttribute("draggable", "true");

        card.addEventListener("dragstart", function (event) {
            dragged = card;
            card.classList.add("dragging");
            event.dataTransfer.effectAllowed = "move";
            try { event.dataTransfer.setData("text/plain", card.getAttribute("data-task-card")); } catch (e) { /* ignorieren */ }
        });

        card.addEventListener("dragend", function () {
            card.classList.remove("dragging");
            dragged = null;
            document.querySelectorAll(".board-column.drag-over").forEach(function (col) {
                col.classList.remove("drag-over");
            });
        });
    });

    document.querySelectorAll("[data-board-column]").forEach(function (column) {
        column.addEventListener("dragover", function (event) {
            if (!dragged) return;
            event.preventDefault();
            column.classList.add("drag-over");
        });

        column.addEventListener("dragleave", function () {
            column.classList.remove("drag-over");
        });

        column.addEventListener("drop", function (event) {
            event.preventDefault();
            column.classList.remove("drag-over");

            if (!dragged) return;

            var taskId = dragged.getAttribute("data-task-card");
            var newStatus = column.getAttribute("data-board-column");
            var list = column.querySelector(".board-cards");

            if (list) list.appendChild(dragged);

            fetch("/api/tasks/" + taskId + "/status", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "X-CSRF-TOKEN": csrfToken()
                },
                body: JSON.stringify({ status: newStatus })
            }).then(function (response) {
                if (!response.ok) {
                    showToast("Der Status konnte nicht gespeichert werden. Bitte Seite neu laden.", "error");
                }
            }).catch(function () {
                showToast("Keine Verbindung – die Änderung wurde nicht gespeichert.", "error");
            });
        });
    });

    /* Statuswechsel zusätzlich barrierefrei über ein Auswahlfeld. */
    document.querySelectorAll("[data-task-status]").forEach(function (select) {
        select.addEventListener("change", function () {
            var taskId = select.getAttribute("data-task-status");
            fetch("/api/tasks/" + taskId + "/status", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "X-CSRF-TOKEN": csrfToken()
                },
                body: JSON.stringify({ status: select.value })
            }).then(function (response) {
                if (response.ok) {
                    select.closest(".board-card")?.setAttribute("data-status", select.value);
                } else {
                    showToast("Der Status konnte nicht gespeichert werden.", "error");
                }
            }).catch(function () {
                showToast("Keine Verbindung – die Änderung wurde nicht gespeichert.", "error");
            });
        });
    });

    /* ------------------------------------------------------------ Benachrichtigungen */
    var bell = document.querySelector("[data-notification-toggle]");
    var panel = document.querySelector("[data-notification-panel]");

    if (bell && panel) {
        bell.addEventListener("click", function (event) {
            event.stopPropagation();
            var open = panel.classList.toggle("open");
            bell.setAttribute("aria-expanded", open ? "true" : "false");
        });

        document.addEventListener("click", function (event) {
            if (!panel.contains(event.target) && !bell.contains(event.target)) {
                panel.classList.remove("open");
                bell.setAttribute("aria-expanded", "false");
            }
        });
    }

    /* ------------------------------------------------------------ Schnellaufgabe */
    var quickForm = document.querySelector("[data-quick-task]");
    if (quickForm) {
        quickForm.addEventListener("submit", function (event) {
            event.preventDefault();
            var input = quickForm.querySelector('input[name="title"]');
            if (!input || !input.value.trim()) return;

            var body = { title: input.value.trim() };
            var due = quickForm.querySelector('input[name="dueAt"]');
            if (due && due.value) body.dueAt = new Date(due.value).toISOString();
            var priority = quickForm.querySelector('select[name="priority"]');
            if (priority && priority.value) body.priority = priority.value;

            fetch("/api/tasks/quick", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "X-CSRF-TOKEN": csrfToken()
                },
                body: JSON.stringify(body)
            }).then(function (response) {
                if (response.ok) {
                    input.value = "";
                    if (due) due.value = "";
                    showToast("Aufgabe wurde angelegt.", "success");
                    setTimeout(function () { window.location.reload(); }, 550);
                } else {
                    showToast("Die Aufgabe konnte nicht angelegt werden.", "error");
                }
            }).catch(function () {
                showToast("Keine Verbindung – die Aufgabe wurde nicht angelegt.", "error");
            });
        });
    }

    /* ------------------------------------------------------------ Toasts */
    function showToast(message, type) {
        var stack = document.querySelector(".toast-stack");
        if (!stack) {
            stack = document.createElement("div");
            stack.className = "toast-stack";
            stack.setAttribute("role", "status");
            stack.setAttribute("aria-live", "polite");
            document.body.appendChild(stack);
        }

        var toast = document.createElement("div");
        toast.className = "toast " + (type || "");
        toast.textContent = message;
        stack.appendChild(toast);
        setTimeout(function () { toast.remove(); }, 7000);
    }

    /* ------------------------------------------------------------ Aufklappbereiche */
    document.querySelectorAll("[data-toggle-target]").forEach(function (button) {
        button.addEventListener("click", function () {
            var target = document.querySelector(button.getAttribute("data-toggle-target"));
            if (!target) return;
            var open = target.classList.toggle("open");
            button.setAttribute("aria-expanded", open ? "true" : "false");
        });
    });

    /* ------------------------------------------------------------ Zeitzonen-Hinweis */
    document.querySelectorAll("[data-local-time]").forEach(function (el) {
        var utc = el.getAttribute("data-local-time");
        if (!utc) return;
        var date = new Date(utc);
        if (isNaN(date.getTime())) return;
        el.textContent = date.toLocaleString("de-DE", {
            day: "2-digit",
            month: "2-digit",
            year: "numeric",
            hour: "2-digit",
            minute: "2-digit"
        });
    });

    /* ------------------------------------------------------------ Kalender: Drag & Drop (Monat) */
    var calRoot = document.querySelector("[data-calendar-month]");
    if (calRoot) {
        var calDragged = null;

        calRoot.querySelectorAll(".cal-chip[data-event-id]").forEach(function (chip) {
            if (chip.getAttribute("draggable") === "false") return;

            chip.addEventListener("dragstart", function (event) {
                calDragged = chip;
                chip.classList.add("dragging");
                event.dataTransfer.effectAllowed = "move";
                try { event.dataTransfer.setData("text/plain", chip.getAttribute("data-event-id")); } catch (e) { /* ignorieren */ }
            });

            chip.addEventListener("dragend", function () {
                chip.classList.remove("dragging");
                calDragged = null;
                calRoot.querySelectorAll(".cal-day.drag-over").forEach(function (d) { d.classList.remove("drag-over"); });
            });
        });

        calRoot.querySelectorAll("[data-cal-date]").forEach(function (day) {
            day.addEventListener("dragover", function (event) {
                if (!calDragged) return;
                event.preventDefault();
                day.classList.add("drag-over");
            });

            day.addEventListener("dragleave", function () { day.classList.remove("drag-over"); });

            day.addEventListener("drop", function (event) {
                event.preventDefault();
                day.classList.remove("drag-over");
                if (!calDragged) return;

                var startIso = calDragged.getAttribute("data-event-start");
                var target = day.getAttribute("data-cal-date");
                if (!startIso || !target) return;

                var start = new Date(startIso);
                if (isNaN(start.getTime())) return;

                var pad = function (n) { return (n < 10 ? "0" : "") + n; };
                var origLocal = pad(start.getFullYear()) + "-" + pad(start.getMonth() + 1) + "-" + pad(start.getDate());
                var diffDays = Math.round((Date.parse(target + "T00:00:00Z") - Date.parse(origLocal + "T00:00:00Z")) / 86400000);
                if (diffDays === 0) return;

                var newStart = new Date(start.getTime() + diffDays * 86400000);
                var eventId = calDragged.getAttribute("data-event-id");

                fetch("/api/calendar/events/" + eventId + "/move", {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/json",
                        "X-CSRF-TOKEN": csrfToken()
                    },
                    body: JSON.stringify({ startsAt: newStart.toISOString() })
                }).then(function (response) {
                    if (response.ok) {
                        showToast("Termin verschoben.", "success");
                        setTimeout(function () { window.location.reload(); }, 500);
                    } else {
                        showToast("Der Termin konnte nicht verschoben werden.", "error");
                    }
                }).catch(function () {
                    showToast("Keine Verbindung – der Termin wurde nicht verschoben.", "error");
                });
            });
        });
    }

    /* ------------------------------------------------------------ Terminformular */
    document.querySelectorAll("[data-event-form]").forEach(function (form) {
        var orgSelect = form.querySelector("[data-event-org]");
        var roomSelect = form.querySelector("[data-event-room]");
        var participantsSelect = form.querySelector("[data-event-participants]");
        var orgFields = form.querySelector("[data-org-fields]");
        var freqSelect = form.querySelector("[data-recurrence-frequency]");
        var allDay = form.querySelector("#Input_IsAllDay");
        var startInput = form.querySelector("#Input_StartsAt");
        var endInput = form.querySelector("#Input_EndsAt");

        function filterByOrg(select, orgValue) {
            if (!select) return;
            Array.prototype.forEach.call(select.options, function (option) {
                var org = option.getAttribute("data-org");
                if (!org) return; // "Kein Raum"/leere Option bleibt
                var match = orgValue && org === orgValue;
                option.hidden = !match;
                option.disabled = !match;
                if (!match && option.selected) {
                    option.selected = false;
                }
            });
        }

        function syncOrg() {
            var orgValue = orgSelect ? orgSelect.value : "";
            if (orgFields) {
                orgFields.hidden = !orgValue;
            }
            filterByOrg(roomSelect, orgValue);
            filterByOrg(participantsSelect, orgValue);
        }

        function syncRecurrence() {
            var off = !freqSelect || freqSelect.value === "None";
            form.querySelectorAll("[data-recurrence-extra]").forEach(function (el) {
                el.hidden = off;
            });
        }

        function syncAllDay() {
            if (!allDay) return;
            var on = allDay.checked;
            [startInput, endInput].forEach(function (input) {
                if (!input) return;
                input.type = on ? "date" : "datetime-local";
                if (on && input.value && input.value.length > 10) {
                    input.value = input.value.substring(0, 10);
                } else if (!on && input.value && input.value.length === 10) {
                    input.value = input.value + "T09:00";
                }
            });
        }

        if (orgSelect) orgSelect.addEventListener("change", syncOrg);
        if (freqSelect) freqSelect.addEventListener("change", syncRecurrence);
        if (allDay) allDay.addEventListener("change", syncAllDay);

        syncOrg();
        syncRecurrence();
    });
})();
