// RoomBook 共用腳本

// 取消預約對話框：按鈕帶 data-* 資料，填入共用的 #cancelModal
document.addEventListener('click', function (e) {
    const btn = e.target.closest('[data-cancel]');
    if (!btn) return;
    const modalEl = document.getElementById('cancelModal');
    if (!modalEl) return;
    const form = modalEl.querySelector('form');
    form.querySelector('[name=id]').value = btn.dataset.id || '';
    form.querySelector('[name=seriesId]').value = btn.dataset.series || '';
    form.querySelector('[name=date]').value = btn.dataset.date || '';
    form.querySelector('[name=returnUrl]').value = btn.dataset.return || '';
    document.getElementById('cancelTitle').textContent = btn.dataset.title || '';
    const forced = btn.dataset.forced === 'True' || btn.dataset.forced === 'true';
    const reason = document.getElementById('cancelReason');
    reason.required = forced;
    reason.value = '';
    document.getElementById('cancelReasonHint').textContent = forced ? '（強制取消必填，會通知預約人）' : '（選填）';
    bootstrap.Modal.getOrCreateInstance(modalEl).show();
});

// 通用確認：<form data-confirm="訊息">（文字以屬性傳入，避免把使用者資料塞進 inline script）
document.addEventListener('submit', function (e) {
    const msg = e.target.dataset && e.target.dataset.confirm;
    if (msg && !window.confirm(msg)) e.preventDefault();
});
