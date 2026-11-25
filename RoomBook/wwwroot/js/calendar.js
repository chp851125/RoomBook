// 行事曆（FullCalendar）。timeZone 用 UTC 並傳台北牆上時間，顯示不受瀏覽器時區影響。
(function () {
    const cfg = JSON.parse(document.getElementById('cal-config').textContent);
    const el = document.getElementById('calendar');
    const roomSel = document.getElementById('roomSel');
    const floorSel = document.getElementById('floorSel');
    const isMobile = () => window.matchMedia('(max-width: 767.98px)').matches;
    const taipeiNow = () => new Date(Date.now() + 8 * 3600 * 1000);
    const wall = d => d.toISOString().slice(0, 16); // 本身就是台北牆上時間

    const calendar = new FullCalendar.Calendar(el, {
        locale: 'zh-tw',
        timeZone: 'UTC',
        initialView: isMobile() ? 'timeGridDay' : 'timeGridWeek',
        headerToolbar: { left: 'prev,next', center: 'title', right: 'today' },
        buttonText: { today: '今天' },
        height: 'auto',
        now: taipeiNow,
        nowIndicator: true,
        slotMinTime: cfg.slotMin,
        slotMaxTime: cfg.slotMax,
        scrollTime: '08:00:00',
        slotDuration: '00:30:00',
        businessHours: cfg.businessHours,
        selectable: true,
        selectMirror: true,
        selectLongPressDelay: 300,
        allDaySlot: true,
        navLinks: false,
        eventTimeFormat: { hour: '2-digit', minute: '2-digit', hour12: false },
        slotLabelFormat: { hour: '2-digit', minute: '2-digit', hour12: false },
        selectAllow: info => cfg.isAdmin || info.start.getTime() >= taipeiNow().getTime() - 30 * 60000,
        events: (info, success, failure) => {
            const p = new URLSearchParams({ start: info.startStr, end: info.endStr });
            if (roomSel.value) p.set('roomId', roomSel.value);
            else if (floorSel.value) p.set('floor', floorSel.value);
            fetch(cfg.eventsUrl + '&' + p, { headers: { Accept: 'application/json' } })
                .then(r => { if (!r.ok) throw new Error(r.status); return r.json(); })
                .then(success, failure);
        },
        eventDidMount: arg => {
            const p = arg.event.extendedProps;
            if (p && p.organizer) arg.el.title = `${arg.event.title}\n${p.room}｜${p.organizer}${p.recurring ? '（週期）' : ''}`;
        },
        // 拖曳選取較長時段
        select: info => {
            if (info.allDay) return;
            if (info.end - info.start <= 30 * 60000) return; // 單一格由 dateClick 處理
            go(info.start, info.end);
            calendar.unselect();
        },
        // 點選單一空白時段（手機點一下即可）
        dateClick: info => {
            if (calendar.view.type === 'dayGridMonth' || info.allDay) {
                calendar.changeView('timeGridDay', info.date);
                return;
            }
            go(info.date, new Date(info.date.getTime() + 60 * 60000));
        }
    });

    function go(start, end) {
        const q = new URLSearchParams({ start: wall(start), end: wall(end) });
        if (roomSel.value) q.set('roomId', roomSel.value);
        location.href = cfg.createUrl + '?' + q;
    }

    function syncUrl() {
        const q = new URLSearchParams();
        if (roomSel.value) q.set('roomId', roomSel.value);
        else if (floorSel.value) q.set('floor', floorSel.value);
        history.replaceState(null, '', location.pathname + (q.toString() ? '?' + q : ''));
    }

    roomSel.addEventListener('change', () => { if (roomSel.value) floorSel.value = ''; syncUrl(); calendar.refetchEvents(); });
    floorSel.addEventListener('change', () => { if (floorSel.value) roomSel.value = ''; syncUrl(); calendar.refetchEvents(); });

    const buttons = document.querySelectorAll('#viewSwitch [data-view]');
    const markActive = () => buttons.forEach(b => b.classList.toggle('active', b.dataset.view === calendar.view.type));
    buttons.forEach(b => b.addEventListener('click', () => { calendar.changeView(b.dataset.view); }));
    calendar.setOption('datesSet', markActive);

    calendar.render();
    markActive();
})();
