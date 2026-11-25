// 預約表單：即時衝突檢查、快速時長、週期欄位、訪客列、參與者搜尋
(function () {
    const form = document.getElementById('bookingForm');
    if (!form) return;
    const result = document.getElementById('checkResult');
    const q = sel => form.querySelector(sel);
    const date = q('[name="Input.Date"]'), start = q('[name="Input.StartTime"]'), end = q('[name="Input.EndTime"]');
    const room = q('[name="Input.RoomId"]'), recur = q('[name="Input.Recurrence"]');
    const attendees = q('[name="Input.AttendeeCount"]');

    // ---- 即時檢查（防抖，且只採用最新一次回應）----
    let timer = null, seq = 0;
    function scheduleCheck() { clearTimeout(timer); timer = setTimeout(runCheck, 350); }
    async function runCheck() {
        if (!room.value || !date.value || !start.value || !end.value) return;
        const mine = ++seq;
        try {
            const resp = await fetch(form.dataset.checkUrl, { method: 'POST', body: new FormData(form) });
            if (!resp.ok) throw new Error(resp.status);
            const html = await resp.text();
            if (mine === seq) result.innerHTML = html;
        } catch (e) {
            if (mine === seq) result.innerHTML = '<div class="alert alert-secondary py-2 small mb-0">暫時無法檢查，送出時仍會由伺服器驗證。</div>';
        }
    }
    ['change', 'input'].forEach(ev => form.addEventListener(ev, e => {
        const t = e.target;
        if (t.matches('[name="Input.Title"], [name="Input.Description"], #peopleFilter, .guest-row input')) return;
        scheduleCheck();
    }));

    // ---- 快速時長 ----
    document.querySelectorAll('.duration-btn').forEach(b => b.addEventListener('click', () => {
        if (!start.value) return;
        const [h, m] = start.value.split(':').map(Number);
        const total = h * 60 + m + Number(b.dataset.minutes);
        if (total >= 24 * 60) return;
        end.value = String(Math.floor(total / 60)).padStart(2, '0') + ':' + String(total % 60).padStart(2, '0');
        scheduleCheck();
    }));

    // ---- 週期欄位顯示 ----
    function syncRecur() {
        const none = recur.value === 'None';
        form.querySelectorAll('.recur-only').forEach(e => e.classList.toggle('d-none', none));
        form.querySelectorAll('.recur-weekdays').forEach(e => e.classList.toggle('d-none', !(recur.value === 'Weekly' || recur.value === 'BiWeekly')));
    }
    recur.addEventListener('change', () => {
        syncRecur();
        const endDate = q('[name="Input.RecurrenceEndDate"]');
        if (recur.value !== 'None' && !endDate.value && date.value) {
            const d = new Date(date.value + 'T00:00:00'); d.setDate(d.getDate() + 7 * 11); // 預設 12 週
            endDate.value = d.toISOString().slice(0, 10);
        }
    });
    syncRecur();

    // ---- 人數不超過容量的提示 ----
    function capacityHint() {
        const opt = room.selectedOptions[0];
        const cap = opt && Number(opt.dataset.capacity);
        attendees.setCustomValidity(cap && Number(attendees.value) > cap ? `超過容納人數 ${cap} 人` : '');
    }
    room.addEventListener('change', capacityHint); attendees.addEventListener('input', capacityHint);

    // ---- 外部訪客 ----
    const rows = document.getElementById('guestRows');
    function renumber() {
        rows.querySelectorAll('.guest-row').forEach((row, i) =>
            row.querySelectorAll('input').forEach(inp => inp.name = inp.name.replace(/Guests\[\d+\]/, `Guests[${i}]`)));
    }
    document.getElementById('addGuest').addEventListener('click', () => {
        const html = document.getElementById('guestTemplate').innerHTML.replaceAll('__i__', rows.querySelectorAll('.guest-row').length);
        rows.insertAdjacentHTML('beforeend', html);
        rows.lastElementChild.querySelector('input').focus();
    });
    rows.addEventListener('click', e => {
        const btn = e.target.closest('.remove-guest');
        if (btn) { btn.closest('.guest-row').remove(); renumber(); }
    });
    form.addEventListener('submit', renumber);

    // ---- 參與者搜尋與計數 ----
    const filter = document.getElementById('peopleFilter'), counter = document.getElementById('pickedCount');
    const updateCount = () => { const n = form.querySelectorAll('.people-check:checked').length; counter.textContent = n ? `已選 ${n} 人` : ''; };
    filter.addEventListener('input', () => {
        const k = filter.value.trim().toLowerCase();
        form.querySelectorAll('.people-item').forEach(el => el.classList.toggle('d-none', k && !el.dataset.search.toLowerCase().includes(k)));
    });
    form.addEventListener('change', e => { if (e.target.classList.contains('people-check')) updateCount(); });
    updateCount();

    if (room.value) runCheck();
})();
