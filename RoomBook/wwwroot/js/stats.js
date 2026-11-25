// 使用統計儀表板（ECharts）
(function () {
    const d = JSON.parse(document.getElementById('stats-data').textContent);
    const charts = [];
    const init = id => { const c = echarts.init(document.getElementById(id)); charts.push(c); return c; };
    const palette = ['#0d6efd', '#198754', '#6f42c1', '#fd7e14', '#20c997', '#d63384', '#0dcaf0', '#6c757d'];
    const empty = { title: { text: '此期間沒有資料', left: 'center', top: 'middle', textStyle: { color: '#adb5bd', fontSize: 14 } } };

    // 各會議室使用率
    const rooms = init('chRooms');
    rooms.setOption(d.rooms.every(r => r.hours === 0) ? empty : {
        color: palette,
        grid: { left: 8, right: 24, top: 20, bottom: 8, containLabel: true },
        tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' }, formatter: p => `${p[0].name}<br/>使用率 ${p[0].value}%<br/>使用 ${d.rooms[p[0].dataIndex].hours} 小時` },
        xAxis: { type: 'value', max: v => Math.max(100, Math.ceil(v.max / 10) * 10), axisLabel: { formatter: '{value}%' } },
        yAxis: { type: 'category', inverse: true, data: d.rooms.map(r => r.name) },
        series: [{ type: 'bar', data: d.rooms.map(r => r.rate), label: { show: true, position: 'right', formatter: '{c}%' }, itemStyle: { borderRadius: [0, 4, 4, 0] } }]
    });

    // 各部門使用時數
    const depts = init('chDepts');
    depts.setOption(d.departments.length === 0 ? empty : {
        color: palette,
        tooltip: { trigger: 'item', formatter: '{b}<br/>{c} 小時（{d}%）' },
        legend: { bottom: 0 },
        series: [{ type: 'pie', radius: ['38%', '68%'], center: ['50%', '45%'], data: d.departments.map(x => ({ name: x.name, value: x.hours })), label: { formatter: '{b}\n{c} h' } }]
    });

    // 熱門時段熱度圖
    const heat = init('chHeat');
    const days = ['週一', '週二', '週三', '週四', '週五', '週六', '週日'];
    const max = Math.max(1, ...d.heat.map(h => h.hours));
    heat.setOption(d.heat.length === 0 ? empty : {
        tooltip: { position: 'top', formatter: p => `${days[p.value[1]]} ${String(p.value[0]).padStart(2, '0')}:00–${String(p.value[0] + 1).padStart(2, '0')}:00<br/>${p.value[2]} 小時` },
        grid: { left: 8, right: 16, top: 8, bottom: 56, containLabel: true },
        xAxis: { type: 'category', data: d.heatHours, axisLabel: { formatter: v => v + ':00' }, splitArea: { show: true } },
        yAxis: { type: 'category', data: days, inverse: true, splitArea: { show: true } },
        visualMap: { min: 0, max: max, calculable: true, orient: 'horizontal', left: 'center', bottom: 0, inRange: { color: ['#e7f1ff', '#0d6efd', '#052c65'] } },
        series: [{ type: 'heatmap', data: d.heat.map(h => [h.hour, h.day, h.hours]), label: { show: false } }]
    });

    // 簽到率
    const ci = init('chCheck');
    const c = d.checkIn;
    ci.setOption(c.total === 0 ? empty : {
        color: ['#198754', '#dc3545', '#adb5bd'],
        tooltip: { trigger: 'item', formatter: '{b}：{c} 場（{d}%）' },
        legend: { bottom: 0 },
        series: [{ type: 'pie', radius: ['45%', '70%'], center: ['50%', '45%'],
            data: [{ name: '已簽到', value: c.checkedIn }, { name: '未簽到釋出', value: c.noShow }, { name: '其他（未簽到未釋出）', value: c.other }].filter(x => x.value > 0),
            label: { formatter: '{b}\n{c}' } }]
    });

    // 近 6 個月趨勢
    const tr = init('chTrend');
    tr.setOption({
        color: ['#0d6efd', '#198754', '#dc3545'],
        tooltip: { trigger: 'axis' },
        legend: { bottom: 0 },
        grid: { left: 8, right: 16, top: 24, bottom: 40, containLabel: true },
        xAxis: { type: 'category', data: d.trend.months },
        yAxis: [{ type: 'value', name: '小時' }, { type: 'value', name: '場次', minInterval: 1 }],
        series: [
            { name: '使用時數', type: 'bar', data: d.trend.hours, itemStyle: { borderRadius: [4, 4, 0, 0] } },
            { name: '預約場次', type: 'line', yAxisIndex: 1, data: d.trend.bookings, smooth: true },
            { name: '未簽到釋出', type: 'line', yAxisIndex: 1, data: d.trend.noShows, smooth: true }
        ]
    });

    window.addEventListener('resize', () => charts.forEach(x => x.resize()));
})();
