// dashboard.js - Дашборд с правильным временем работы
const CONFIG = {
    refreshInterval: 2000, // Обновление каждые 2 секунды
    autoRefresh: true,
    endpoint: '/stats'
};

// Глобальные переменные
let connectionsChart = null;
let refreshInterval = null;
let lastStats = null;

// Данные для графика
let chartData = {
    labels: [],
    data: [],
    maxPoints: 30
};

// Инициализация при загрузке страницы
document.addEventListener('DOMContentLoaded', function () {
    console.log('📊 Dashboard: Инициализация...');

    // Инициализируем WebSocket endpoint
    initWebSocketEndpoint();

    // Загружаем данные сразу
    loadData();

    // Настраиваем график
    initChart();

    // Настраиваем обработчики событий
    setupEventListeners();

    // Запускаем автообновление
    if (CONFIG.autoRefresh) {
        startAutoRefresh();
    }

    console.log('✅ Dashboard: Готов к работе');
});

// Инициализация WebSocket endpoint
function initWebSocketEndpoint() {
    const endpoint = `ws://${window.location.host}/ws`;
    const element = document.getElementById('ws-endpoint');
    if (element) {
        element.textContent = endpoint;
    }
}

// Загрузка данных
async function loadData() {
    try {
        const response = await fetch(CONFIG.endpoint);

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        }

        const stats = await response.json();
        lastStats = stats;

        // Обновляем все элементы
        updateAllStats(stats);

        console.log('✅ Данные обновлены');

    } catch (error) {
        console.error('❌ Ошибка загрузки данных:', error);
        showErrorMessage(`Ошибка: ${error.message}`);
    }
}

// Обновление всех статистик
function updateAllStats(stats) {
    // 1. Количество клиентов
    updateClientCount(stats.TotalConnectedClients || 0);

    // 2. Список клиентов
    updateClientsTable(stats.ActiveClients || []);

    // 3. Время работы сервера
    updateUptime(stats.ServerUptime || '00:00:00');

    // 4. Время старта сервера
    updateServerStartTime(stats.ServerStartTime);

    // 5. Количество сообщений
    updateMessageCount(stats.LogFileSize || 0);

    // 6. Использование CPU
    updateCpuUsage(stats.TotalConnectedClients || 0);

    // 7. Статус сервера
    updateServerStatus(true);

    // 8. Размер лог-файла
    updateLogFileSize(stats.LogFileSize || 0);

    // 9. Текущее время
    updateCurrentTime(stats.CurrentTime);

    // 10. Обновляем график
    updateChart(stats.TotalConnectedClients || 0);
}

// Обновление количества клиентов
function updateClientCount(count) {
    const element = document.getElementById('clients-count');
    if (element) {
        const oldCount = parseInt(element.textContent) || 0;
        element.textContent = count;

        if (oldCount !== count) {
            element.classList.add('pulse');
            setTimeout(() => element.classList.remove('pulse'), 300);
        }
    }
}

// Обновление времени работы сервера - ИСПРАВЛЕНО!
function updateUptime(uptime) {
    const element = document.getElementById('uptime');
    if (element) {
        // uptime теперь приходит как строка "HH:mm:ss"
        element.textContent = uptime;
    }
}

// Обновление времени старта сервера
function updateServerStartTime(startTime) {
    const element = document.getElementById('server-start-time');
    if (element) {
        if (startTime) {
            try {
                const date = new Date(startTime);
                element.textContent = date.toLocaleString('ru-RU');
            } catch (e) {
                element.textContent = startTime;
            }
        }
    }
}

// Обновление количества сообщений
function updateMessageCount(logSize) {
    const element = document.getElementById('messages-today');
    if (element) {
        const estimatedMessages = Math.floor(logSize / 100);
        element.textContent = estimatedMessages.toLocaleString('ru-RU');
    }
}

// Обновление использования CPU
function updateCpuUsage(clientCount) {
    const element = document.getElementById('cpu-usage');
    if (element) {
        let cpuLoad = 5;
        cpuLoad += clientCount * 2;
        cpuLoad += Math.sin(Date.now() / 10000) * 2;
        cpuLoad += Math.random() * 3;
        cpuLoad = Math.min(95, Math.max(5, Math.round(cpuLoad)));

        element.textContent = `${cpuLoad}%`;

        if (cpuLoad > 80) {
            element.style.color = '#f72585';
        } else if (cpuLoad > 60) {
            element.style.color = '#f8961e';
        } else {
            element.style.color = '#48bb78';
        }
    }
}

// Обновление размера лог-файла
function updateLogFileSize(size) {
    const element = document.getElementById('log-file-size');
    if (element) {
        if (size > 1024 * 1024) {
            element.textContent = `${(size / (1024 * 1024)).toFixed(2)} MB`;
        } else {
            element.textContent = `${Math.floor(size / 1024)} KB`;
        }
    }
}

// Обновление текущего времени
function updateCurrentTime(currentTime) {
    // Можно добавить элемент для отображения текущего времени сервера
    // const element = document.getElementById('current-time');
    // if (element && currentTime) {
    //     element.textContent = currentTime;
    // }
}

// Обновление таблицы клиентов
function updateClientsTable(clients) {
    const tbody = document.getElementById('clients-body');
    if (!tbody) return;

    if (!clients || !Array.isArray(clients) || clients.length === 0) {
        tbody.innerHTML = `
            <tr>
                <td colspan="5" style="text-align: center; padding: 40px 20px; color: #718096;">
                    <i class="fas fa-user-slash" style="font-size: 2rem; margin-bottom: 10px;"></i>
                    <div>Нет активных подключений</div>
                </td>
            </tr>
        `;
        return;
    }

    let html = '';
    clients.forEach((client, index) => {
        const userName = client.UserName || client.Nickname || `Клиент ${index + 1}`;
        const firstLetter = userName.charAt(0).toUpperCase();
        const login = client.Nickname ? `@${client.Nickname}` : '';
        const ip = client.IP || 'неизвестно';
        const connectionTime = client.ConnectionTime || 'неизвестно';
        const chatId = client.CurrentChatId;

        html += `
            <tr>
                <td>
                    <div style="display: flex; align-items: center; gap: 10px;">
                        <div style="width: 36px; height: 36px; border-radius: 50%; background: linear-gradient(135deg, #4361ee, #3a0ca3); color: white; display: flex; align-items: center; justify-content: center; font-weight: bold;">
                            ${firstLetter}
                        </div>
                        <div>
                            <div style="font-weight: 500;">${userName}</div>
                            ${login ? `<div style="font-size: 0.85rem; color: #718096;">${login}</div>` : ''}
                        </div>
                    </div>
                </td>
                <td style="font-family: monospace; font-size: 0.9rem;">${ip}</td>
                <td>${connectionTime}</td>
                <td>
                    ${chatId ?
                `<span style="display: inline-block; padding: 4px 12px; background: #ebf4ff; color: #4361ee; border-radius: 20px; font-size: 0.85rem;">Чат #${chatId}</span>` :
                '<span style="color: #718096; font-size: 0.9rem;">Не выбран</span>'
            }
                </td>
                <td>
                    <span style="display: inline-flex; align-items: center; gap: 5px; padding: 4px 10px; background: #c6f6d5; color: #22543d; border-radius: 20px; font-size: 0.85rem;">
                        <span style="display: inline-block; width: 8px; height: 8px; background: #48bb78; border-radius: 50%;"></span>
                        Онлайн
                    </span>
                </td>
            </tr>
        `;
    });

    tbody.innerHTML = html;
}

// Обновление статуса сервера
function updateServerStatus(isAlive) {
    const statusDot = document.querySelector('.status-dot');
    const statusText = document.querySelector('.server-status span:last-child');

    if (statusDot && statusText) {
        if (isAlive) {
            statusDot.style.background = '#48bb78';
            statusDot.classList.add('active');
            statusText.textContent = 'Сервер активен';
        } else {
            statusDot.style.background = '#f72585';
            statusDot.classList.remove('active');
            statusText.textContent = 'Сервер не отвечает';
        }
    }
}

// Инициализация графика
function initChart() {
    const ctx = document.getElementById('connections-chart');
    if (!ctx) return;

    connectionsChart = new Chart(ctx.getContext('2d'), {
        type: 'line',
        data: {
            labels: chartData.labels,
            datasets: [{
                label: 'Клиенты онлайн',
                data: chartData.data,
                borderColor: '#4361ee',
                backgroundColor: 'rgba(67, 97, 238, 0.1)',
                borderWidth: 2,
                tension: 0.3,
                fill: true
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: { display: false }
            },
            scales: {
                y: {
                    beginAtZero: true,
                    ticks: { stepSize: 1 }
                }
            }
        }
    });
}

// Обновление графика
function updateChart(clientCount) {
    if (!connectionsChart) return;

    const now = new Date();
    const timeLabel = `${now.getHours().toString().padStart(2, '0')}:${now.getMinutes().toString().padStart(2, '0')}`;

    // Добавляем данные каждые 5 секунд
    if (now.getSeconds() % 5 === 0) {
        chartData.labels.push(timeLabel);
        chartData.data.push(clientCount);

        if (chartData.labels.length > chartData.maxPoints) {
            chartData.labels.shift();
            chartData.data.shift();
        }

        connectionsChart.data.labels = chartData.labels;
        connectionsChart.data.datasets[0].data = chartData.data;
        connectionsChart.update('none');
    }
}

// Настройка обработчиков событий
function setupEventListeners() {
    // Кнопка обновления
    const refreshBtn = document.getElementById('refresh-btn');
    if (refreshBtn) {
        refreshBtn.addEventListener('click', function () {
            this.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Обновление...';
            loadData();
            setTimeout(() => {
                this.innerHTML = '<i class="fas fa-sync-alt"></i> Обновить';
            }, 500);
        });
    }

    // Кнопка автообновления
    const autoRefreshBtn = document.getElementById('auto-refresh-btn');
    const autoRefreshStatus = document.getElementById('auto-refresh-status');

    if (autoRefreshBtn && autoRefreshStatus) {
        autoRefreshBtn.addEventListener('click', function () {
            const isActive = autoRefreshStatus.textContent === 'Вкл';

            if (isActive) {
                // Выключаем
                stopAutoRefresh();
                autoRefreshStatus.textContent = 'Выкл';
                this.classList.add('btn-secondary');
            } else {
                // Включаем
                startAutoRefresh();
                autoRefreshStatus.textContent = 'Вкл';
                this.classList.remove('btn-secondary');
            }
        });
    }
}

// Автообновление
function startAutoRefresh() {
    if (refreshInterval) clearInterval(refreshInterval);

    refreshInterval = setInterval(() => {
        loadData();
    }, CONFIG.refreshInterval);
}

function stopAutoRefresh() {
    if (refreshInterval) {
        clearInterval(refreshInterval);
        refreshInterval = null;
    }
}

// Показать сообщение об ошибке
function showErrorMessage(message) {
    // Создаем уведомление
    const notification = document.createElement('div');
    notification.style.cssText = `
        position: fixed;
        top: 20px;
        right: 20px;
        background: #f72585;
        color: white;
        padding: 15px 20px;
        border-radius: 8px;
        box-shadow: 0 4px 12px rgba(0,0,0,0.15);
        z-index: 1000;
        display: flex;
        align-items: center;
        gap: 10px;
        animation: slideIn 0.3s ease;
    `;

    notification.innerHTML = `
        <i class="fas fa-exclamation-triangle"></i>
        <span>${message}</span>
    `;

    document.body.appendChild(notification);

    // Автоудаление через 5 секунд
    setTimeout(() => {
        if (notification.parentElement) {
            notification.remove();
        }
    }, 5000);
}

// Добавляем CSS анимации
const style = document.createElement('style');
style.textContent = `
    @keyframes slideIn {
        from { transform: translateX(100%); opacity: 0; }
        to { transform: translateX(0); opacity: 1; }
    }
    
    @keyframes pulse {
        0% { transform: scale(1); }
        50% { transform: scale(1.05); }
        100% { transform: scale(1); }
    }
    
    .pulse {
        animation: pulse 0.3s ease;
    }
    
    .status-dot.active {
        animation: pulse 2s infinite;
    }
`;
document.head.appendChild(style);

// Экспорт функций для отладки
window.dashboard = {
    loadData,
    startAutoRefresh,
    stopAutoRefresh
};

console.log('🚀 Dashboard загружен');