(() => {
  const I18n = window.CubicI18n;
  const state = {
    services: [], devices: [], selectedDeviceIp: '', control: null, syncedLanguageKey: '',
    catalog: [], catalogDeviceIp: '', catalogLoading: false, currentStoreFilter: 'all', storePendingInstalls: new Map(), pcStoreProgress: new Map(), pcStoreCached: new Map(), storePollTimer: null, firmwarePollTimer: null, provision: null, logs: [], currentPage: 'home', currentControlTab: 'apps',
    serial: null, serialText: '', currentLogView: 'app', currentDeveloperTab: 'lua', mediaKind: 'image',
    fs: { deviceIp:'', path:'/sd/images', items:[], selected:null, previewText:'' }, fsClipboard:null, loadedLuaCode: '', forceAppFrameReload: false
  };
  const pages = {
    home: ['设备总览', '统一连接并管理 Clocteck Cubic'],
    setup: ['添加设备', '连接设备热点并完成首次配网'],
    store: ['应用商店', '从服务器读取应用信息并选择设备或 PC 下载'],
    control: ['设备控制', '软件内置界面通过设备 API 传输数据'],
    files: ['文件管理', '管理设备图片、GIF、音乐、歌词和应用文件'],
    serial: ['串口工具', '连接设备串口并实时读取输出信息'],
    devtools: ['设备开发工具', '编辑并运行 DevRun Lua 代码'],
    logs: ['运行日志', '查看软件运行事件和错误信息'],
    about: ['关于', 'Clocteck Cubic Center'],
  };

  const q = selector => document.querySelector(selector);
  const qa = selector => [...document.querySelectorAll(selector)];
  const post = (action, payload = {}) => window.chrome?.webview?.postMessage({ action, payload });
  const escapeHtml = value => String(value ?? '').replace(/[&<>'"]/g, char => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', "'":'&#39;', '"':'&quot;' })[char]);
  const formatBytes = value => value ? `${(Number(value) / 1024 / 1024 / 1024).toFixed(1)} GB` : '--';
  const selectedDevice = () => state.devices.find(device => device.ipAddress === state.selectedDeviceIp) || null;

  function gotoPage(name) {
    if (!pages[name]) return;
    state.currentPage = name;
    const deviceSection = name === 'control' || name === 'files' || name === 'devtools';
    qa('.page').forEach(page => page.classList.toggle('active', page.id === `page-${name}`));
    qa('.nav-item').forEach(item => item.classList.toggle('active', item.dataset.page === name || (item.dataset.page === 'control' && deviceSection)));
    q('#controlSubmenu').classList.toggle('visible', deviceSection);
    q('#controlRefreshButton').classList.toggle('hidden', !deviceSection);
    qa('[data-control-page]').forEach(item => item.classList.toggle('active', item.dataset.controlPage === name));
    qa('[data-control-tab]').forEach(item => item.classList.toggle('active', name === 'control' && item.dataset.controlTab === state.currentControlTab));
    q('#pageHeading').classList.remove('hidden');
    q('#openDeviceControlButton').classList.toggle('hidden', name === 'control');
    q('#pageTitle').textContent = pages[name][0];
    q('#pageSubtitle').textContent = pages[name][1];
    I18n.localize(q('header'));
    if (name === 'control' && state.selectedDeviceIp) post('device.control.refresh', { ip: state.selectedDeviceIp });
    if (name === 'store' && state.selectedDeviceIp) {
      post('device.control.refresh', { ip: state.selectedDeviceIp });
      if (!state.catalog.length && !state.catalogLoading) { state.catalogLoading = true; post('device.store.load'); }
    }
    if (name === 'files' && state.selectedDeviceIp && state.fs.deviceIp !== state.selectedDeviceIp) post('device.fs.list', { path:state.fs.path || '/sd/images' });
    if (name === 'serial') post('serial.refresh');
  }

  function receive(message) {
    const { type, payload } = message || {};
    if (type === 'app.bootstrap') renderBootstrap(payload || {});
    else if (type === 'system.status') { renderWifi(payload?.wifi); renderStats(payload?.stats); }
    else if (type === 'wifi.networks') renderNetworks(payload || []);
    else if (type === 'provision.status') renderProvision(payload);
    else if (type === 'services.state') renderServices(payload || []);
    else if (type === 'device.list') renderDeviceList(payload || {});
    else if (type === 'device.found') updateFoundDevice(payload);
    else if (type === 'device.discovery') renderDiscovery(payload || {});
    else if (type === 'device.control.open') { gotoPage('control'); setControlStatus('working', I18n.t('正在连接设备')); }
    else if (type === 'device.control.status') setControlStatus(payload?.status, payload?.status === 'working' ? I18n.t('正在读取设备') : payload?.message);
    else if (type === 'device.control') renderControl(payload || {});
    else if (type === 'device.app.starting') renderAppStarting(payload || {});
    else if (type === 'device.action') renderDeviceAction(payload || {});
    else if (type === 'device.store.status') renderStoreStatus(payload || {});
    else if (type === 'device.store') renderStore(payload || {});
    else if (type === 'device.store.pc-progress') renderPcStoreProgress(payload || {});
    else if (type === 'device.store.pc-cache') renderPcStoreCache(payload || {});
    else if (type === 'device.firmware') {
      renderFirmwareUpdate(payload?.result?.firmware_update || {});
      toast(payload?.action === 'update' ? I18n.t('已开始安装固件更新') : I18n.t('固件更新状态已刷新'));
    }
    else if (type === 'device.settings.saved') { q('#settingsSaveHint').textContent = payload?.message || '已保存'; toast(payload?.message || '设备设置已保存'); }
    else if (type === 'device.language.synced') state.syncedLanguageKey = `${payload?.ip || state.selectedDeviceIp}:${payload?.language || I18n.language}`;
    else if (type === 'holoMonitor.config') renderMonitorConfig(payload);
    else if (type === 'holopet.config') renderHolopetConfig(payload);
    else if (type === 'smtcMusic.config') renderSmtcMusicConfig(payload);
    else if (type === 'log.entry') appendLog(payload);
    else if (type === 'serial.status') renderSerialStatus(payload || {});
    else if (type === 'serial.data') appendSerialText(payload || {});
    else if (type === 'device.fs.status') renderDeviceFsStatus(payload || {});
    else if (type === 'device.fs.list') renderDeviceFiles(payload || {});
    else if (type === 'device.fs.preview') renderDeviceFilePreview(payload || {});
    else if (type === 'device.lua.code') renderLuaCode(payload || {});
    else if (type === 'device.lua.saved') renderLuaSaved(payload || {});
    else if (type === 'app.error') {
      state.catalogLoading = false;
      state.storePendingInstalls.clear();
      state.pcStoreProgress.clear();
      clearTimeout(state.storePollTimer);
      renderStoreGrid();
      toast(payload?.message || '操作失败', true);
    }
  }

  function renderBootstrap(data) {
    q('#version').textContent = data.version || '0.1.0';
    renderWifi(data.wifi);
    renderStats(data.stats);
    renderServices(data.services || []);
    renderProvision(data.provision);
    state.logs = data.logs || [];
    renderLogs();
    renderDeviceList({ devices: data.devices || [], selectedDeviceIp: data.selectedDeviceIp || '' });
    renderSerialStatus(data.serial || {});
    q('#headerLanguageSelect').value = I18n.language;
  }

  function renderWifi(wifi) {
    const connected = Boolean(wifi?.ipv4Address || wifi?.ssid || wifi?.displayName);
    const name = wifi?.displayName || wifi?.ssid || wifi?.interfaceName || I18n.t('已连接');
    const type = wifi?.connectionType || (wifi?.ssid ? 'Wi-Fi' : I18n.t('网络'));
    q('#sideWifi').textContent = connected ? `${type} · ${name}` : I18n.t('未连接');
    q('#sideWifiDot').className = `dot ${connected ? 'online' : 'muted'}`;
    q('#wifiValue').textContent = connected ? name : I18n.t('未连接');
    const signal = wifi?.signalQuality == null ? '' : `${wifi.signalQuality}% · `;
    q('#wifiDetail').textContent = connected ? `${type} · ${signal}${wifi.ipv4Address || I18n.t('正在获取 IP')}` : I18n.t('请检查电脑网络');
  }

  function renderStats(stats) {
    if (!stats) return;
    const cpu = Math.max(0, Math.min(100, Number(stats.cpuPercent || 0)));
    const memory = stats.memoryTotalBytes ? Math.round(stats.memoryUsedBytes * 100 / stats.memoryTotalBytes) : 0;
    q('#cpuValue').textContent = `${cpu.toFixed(0)}%`;
    q('#cpuMeter').style.width = `${cpu}%`;
    q('#memoryValue').textContent = `${memory}%`;
    q('#memoryDetail').textContent = `${formatBytes(stats.memoryUsedBytes)} / ${formatBytes(stats.memoryTotalBytes)}`;
  }

  function renderDeviceList(payload) {
    state.devices = Array.isArray(payload.devices) ? payload.devices : [];
    state.selectedDeviceIp = payload.selectedDeviceIp || '';
    const select = q('#headerDeviceSelect');
    select.innerHTML = '<option value="">未选择设备</option>' + state.devices.map(device =>
      `<option value="${escapeHtml(device.ipAddress)}">${escapeHtml(device.name || 'Clocteck Cubic')} · ${escapeHtml(device.ipAddress)}</option>`).join('');
    select.value = state.selectedDeviceIp;

    const active = selectedDevice();
    q('#deviceIp').textContent = active?.ipAddress || '尚未选择';
    q('#deviceHost').textContent = active ? (active.online ? '设备在线' : '设备暂未响应') : 'IP 用于区分设备';
    q('#devicePill').className = `pill ${active?.online ? 'online' : 'offline'}`;
    q('#devicePill').innerHTML = `<span class="dot"></span><span>${active ? (active.online ? '在线' : '未连接') : '设备未连接'}</span>`;
    q('#developerDeviceBadge').textContent = active?.ipAddress || I18n.t('请选择设备');
    q('#developerDeviceBadge').className = `status-tag ${active?.online ? 'running' : ''}`;
    q('#fileManagerDeviceBadge').textContent = active?.ipAddress || I18n.t('请选择设备');
    q('#fileManagerDeviceBadge').className = `status-tag ${active?.online ? 'running' : ''}`;

    renderOverviewDevices();
    if ((state.currentPage === 'files' || state.currentPage === 'devtools') && active && state.fs.deviceIp !== active.ipAddress) {
      post('device.fs.list', { path:state.fs.path || '/sd/images' });
    }

    const host = q('#deviceList');
    if (!host) { syncLanguageToDevice(); return; }
    if (!state.devices.length) {
      host.innerHTML = '<div class="panel empty-state">尚未保存设备，可扫描局域网或手动输入 IP。</div>';
      return;
    }
    host.innerHTML = state.devices.map(device => {
      const selected = device.ipAddress === state.selectedDeviceIp;
      return `<article class="panel saved-device ${selected ? 'selected' : ''}">
        <div class="device-render small"><div class="screen">CUBIC</div></div>
        <div class="saved-device-copy"><div class="row"><div><span class="eyebrow">${selected ? '当前设备' : '已保存设备'}</span><h3>${escapeHtml(device.name || 'Clocteck Cubic')}</h3></div><span class="status-tag ${device.online ? 'running' : ''}">${device.online ? '在线' : '未连接'}</span></div>
        <div class="device-address">${escapeHtml(device.ipAddress)}</div><p>${escapeHtml(I18n.format('设备 ID：{0} · 最近发现：{1}', device.deviceId || '--', formatTime(device.lastSeen)))}</p>
        <div class="button-row"><button class="button primary device-control" data-ip="${escapeHtml(device.ipAddress)}">进入控制</button><button class="button secondary device-select-action" data-ip="${escapeHtml(device.ipAddress)}">设为当前</button><button class="button ghost device-monitor" data-ip="${escapeHtml(device.ipAddress)}">配置 PC 监控</button><button class="button danger device-remove" data-ip="${escapeHtml(device.ipAddress)}">移除</button></div></div>
      </article>`;
    }).join('');
    qa('.device-control').forEach(button => button.addEventListener('click', () => post('device.openControl', { ip: button.dataset.ip })));
    qa('.device-select-action').forEach(button => button.addEventListener('click', () => post('device.select', { ip: button.dataset.ip })));
    qa('.device-monitor').forEach(button => button.addEventListener('click', () => { post('device.select', { ip: button.dataset.ip }); post('holoMonitor.configure', { ip: button.dataset.ip }); }));
    qa('.device-remove').forEach(button => button.addEventListener('click', () => post('device.remove', { ip: button.dataset.ip })));
    I18n.localize(host);
  }

  function renderOverviewDevices() {
    const host = q('#overviewDeviceGrid');
    if (!host) return;
    const connected = state.devices.filter(device => device.online);
    if (!connected.length) {
      host.innerHTML = '<div class="panel empty-state">当前没有在线设备，可在“设备”页面扫描或手动连接。</div>';
      I18n.localize(host);
      return;
    }
    host.innerHTML = connected.map(device => {
      const selected = device.ipAddress === state.selectedDeviceIp;
      const app = device.currentAppName || device.currentAppId || 'Launcher';
      const rssi = Number(device.wifiRssi);
      const hasRssi = Number.isFinite(rssi) && rssi < 0;
      const strength = !hasRssi ? 0 : rssi >= -55 ? 4 : rssi >= -65 ? 3 : rssi >= -75 ? 2 : 1;
      return `<article class="panel overview-device-card ${selected ? 'selected' : ''}" data-ip="${escapeHtml(device.ipAddress)}"><div class="overview-card-top"><div><span>${selected ? '当前设备' : '在线设备'}</span><strong>${escapeHtml(device.name || 'Clocteck Cubic')}</strong></div><span class="dot online"></span></div><div class="overview-device-ip">${escapeHtml(device.ipAddress)}</div><div class="overview-card-details"><div class="overview-app"><span>当前应用</span><b>${escapeHtml(app)}</b></div><div class="overview-signal"><div><span>Wi-Fi</span><b>${hasRssi ? `${rssi} dBm` : 'RSSI --'}</b></div><div class="rssi-bars s${strength}"><i></i><i></i><i></i><i></i></div></div></div></article>`;
    }).join('');
    qa('#overviewDeviceGrid .overview-device-card').forEach(card => card.addEventListener('click', () => post('device.openControl', { ip: card.dataset.ip })));
    I18n.localize(host);
  }

  function updateFoundDevice(device) {
    if (!device?.ipAddress) return;
    toast(`发现设备 ${device.ipAddress}`);
  }

  function renderDiscovery(result) {
    const isError = result.status === 'error' || result.status === 'not-found';
    toast(result.message || '正在发现设备', isError);
  }

  function renderNetworks(networks) {
    const box = q('#networkList');
    if (!networks.length) {
      box.className = 'network-list empty';
      box.innerHTML = '<p>没有发现设备热点。请确认设备已进入配网模式，然后重新扫描。</p>';
      return;
    }
    box.className = 'network-list';
    box.innerHTML = networks.map(network => {
      const strength = Math.max(1, Math.min(4, Math.ceil(Number(network.signalQuality || 0) / 25)));
      return `<div class="network-entry"><div><h4>${escapeHtml(network.ssid)}</h4><span>${network.securityEnabled ? '需要密码或已有配置' : '设备开放热点'} · 信号 ${network.signalQuality}%</span></div><div class="signal s${strength}"><i></i><i></i><i></i><i></i></div><button class="button primary connect-ap" data-ssid="${escapeHtml(network.ssid)}">连接</button></div>`;
    }).join('');
    qa('.connect-ap').forEach(button => button.addEventListener('click', () => post('provision.start', { ssid: button.dataset.ssid })));
    I18n.localize(box);
  }

  function renderProvision(snapshot) {
    if (!snapshot) return;
    state.provision = snapshot;
    const progress = Number(snapshot.progress || 0);
    q('#setupPercent').textContent = `${progress}%`;
    q('#setupProgress').style.width = `${progress}%`;
    q('#setupMessage').textContent = snapshot.message || '准备连接设备';
    q('#setupHint').textContent = stageHint(snapshot.stage);
    q('#setupIcon').textContent = progress >= 100 ? '✓' : String(Math.max(1, Math.ceil(progress / 20))).padStart(2, '0');
    q('#cancelButton').classList.toggle('hidden', !snapshot.canCancel);
    q('#forceButton').classList.toggle('hidden', !snapshot.canForceComplete);
    q('#scanButton').disabled = ['scanning','connecting-ap','restoring','discovering','subnet-scan'].includes(snapshot.stage);
    updateSteps(snapshot.stage);
    if (snapshot.stage === 'complete') toast('设备配网成功');
    if (snapshot.stage === 'error') toast(snapshot.message, true);
  }

  function stageHint(stage) {
    return ({ idle:'程序会保存当前 Wi-Fi，再连接设备热点并打开配网页面。', scanning:'正在通过 Windows WLAN 接口扫描附近热点。', ready:'选择需要配置的设备热点。', 'not-found':'请让设备进入配网模式并靠近电脑。', 'connecting-ap':'电脑网络会短暂切换，请不要关闭程序。', provisioning:'请在已打开的设备页面中选择目标 Wi-Fi 并保存。', 'leaving-ap':'等待设备退出配置热点。', restoring:'正在重新连接配网前的 Wi-Fi。', discovering:'正在按 IP 查找设备。', 'subnet-scan':'正在扫描当前局域网并区分多台设备。', complete:'电脑和设备已经回到同一局域网。', error:'可重试当前流程，或取消并恢复电脑网络。', cancelled:'网络恢复操作已经结束。' })[stage] || '正在处理…';
  }

  function updateSteps(stage) {
    const groups = [['scanning','ready','not-found'],['connecting-ap'],['provisioning','leaving-ap'],['restoring'],['discovering','subnet-scan','complete']];
    let active = groups.findIndex(group => group.includes(stage));
    if (active < 0) active = 0;
    qa('#stepList li').forEach((item, index) => { item.classList.toggle('done', index < active || stage === 'complete'); item.classList.toggle('active', index === active && stage !== 'complete'); });
  }

  function setControlStatus(status = 'idle', message = '') {
    const button = q('#controlRefreshButton');
    if (button) {
      button.disabled = status === 'working';
      button.textContent = status === 'working' ? I18n.t('正在读取设备') : I18n.t('刷新数据');
      button.title = message || '';
    }
    if (status === 'error' && message) toast(message, true);
  }

  function renderAppStarting(payload) {
    state.forceAppFrameReload = true;
    setControlStatus('working', I18n.t('正在等待应用控制页'));
    const pane = q('#appEmbeddedPane');
    const frame = q('#embeddedAppFrame');
    pane.classList.add('empty');
    frame.removeAttribute('src');
    frame.dataset.url = '';
    q('#embeddedAppTitle').textContent = I18n.t('应用启动中');
    q('#embeddedAppUrl').textContent = payload?.id || '';
    q('#embeddedAppPlaceholder b').textContent = I18n.t('正在等待应用控制页');
    q('#embeddedAppPlaceholder p').textContent = I18n.t('设备完成应用初始化后会自动打开控制页面。');
  }

  function renderDeviceAction(payload) {
    if (payload?.action === 'launch') {
      toast(payload.controlReady === false ? I18n.t('应用已启动，控制页可稍后刷新重试') : I18n.t('应用已启动'));
      return;
    }
    toast(I18n.t('设备操作已完成'));
  }

  function renderControl(snapshot) {
    state.control = snapshot;
    const ip = snapshot.ip || state.selectedDeviceIp;
    if (state.catalogDeviceIp !== ip) {
      state.catalog = [];
      state.catalogDeviceIp = ip;
      state.catalogLoading = false;
      state.storePendingInstalls.clear();
      clearTimeout(state.storePollTimer);
    }
    state.selectedDeviceIp = ip;
    setControlStatus('success', I18n.t('设备已连接'));
    renderDeviceApps(snapshot.state || {});
    renderDeviceSettings(snapshot.settings || {}, snapshot.display || null, snapshot.schedule || null, snapshot.state || {});
    renderDeviceServices(snapshot.state || {});
    if (state.catalog.length) renderStoreGrid();
    if (!state.catalog.length && !state.catalogLoading) {
      state.catalogLoading = true;
      post('device.store.load');
    }
  }

  function installedItems(deviceState) {
    const installed = Array.isArray(deviceState?.installed_apps) ? deviceState.installed_apps : [];
    const fallback = Array.isArray(deviceState?.apps) ? deviceState.apps : [];
    const source = installed.length ? installed.concat(fallback) : fallback;
    const seen = new Set();
    return source.filter(item => { const id = String(item?.id || ''); if (!id || seen.has(id)) return false; seen.add(id); return true; });
  }

  function renderDeviceApps(deviceState) {
    const currentId = deviceState?.current_app?.id || '';
    const currentRoute = deviceState?.current_route_base ? normalizeRoute(deviceState.current_route_base) : '';
    const currentHasControls = Boolean(currentRoute && deviceState?.current_webui !== false);
    const apps = installedItems(deviceState).filter(item => String(item.kind || '').toLowerCase() !== 'service' && item.id !== 'launcher');
    q('#exitCurrentApp').classList.toggle('hidden', !currentId);
    q('#deviceAppGrid').innerHTML = apps.length ? apps.map(app => {
      const current = app.id === currentId;
      const storeItem = state.catalog.find(item => item.id === app.id);
      return `<article class="panel control-card device-app-row ${current ? 'current' : ''}"><div class="control-card-head">${appIconMarkup(storeItem || app)}<div><h3>${escapeHtml(app.name || app.id)}</h3><p>${escapeHtml(app.id || '')} · ${escapeHtml(app.version || I18n.t('未知版本'))}</p></div>${current ? '<span class="status-tag running">运行中</span>' : ''}</div><div class="button-row device-app-actions">${current && currentHasControls ? `<button class="button secondary embed-device-path" data-path="${escapeHtml(currentRoute)}" data-title="${escapeHtml(app.name || app.id)}">控制</button>` : current ? '<span class="service-no-controls">无控制页</span>' : ''}<button class="button ${current ? 'danger exit-device-app' : 'primary launch-device-app'}" data-id="${escapeHtml(app.id)}">${current ? '退出' : '打开'}</button></div></article>`;
    }).join('') : '<div class="panel empty-state">设备没有返回可显示的应用。</div>';
    qa('.launch-device-app').forEach(button => button.addEventListener('click', () => post('device.app.launch', {
      id:button.dataset.id,
      language:I18n.language,
      weatherAddress:q('#weatherAddress')?.value?.trim() || state.control?.settings?.weather_address || state.control?.settings?.weatherAddress || ''
    })));
    qa('.exit-device-app').forEach(button => button.addEventListener('click', () => post('device.app.exit')));
    qa('#deviceAppGrid .embed-device-path').forEach(button => button.addEventListener('click', () => showEmbeddedDevicePage('app', button.dataset.path, button.dataset.title, '', true)));
    const currentName = deviceState?.current_app?.name || apps.find(app => app.id === currentId)?.name || currentId;
    if (currentHasControls) showEmbeddedDevicePage('app', currentRoute, currentName, currentId);
    else if (currentId) showNoAppControlPage(currentName, currentId);
    else clearEmbeddedDevicePage('app');
    bindIconFallbacks(q('#deviceAppGrid'));
    I18n.localize(q('#deviceAppGrid'));
  }

  function showEmbeddedDevicePage(kind, path, title, appId = '', forceReload = false) {
    const normalized = normalizeRoute(path);
    if (!normalized || !state.selectedDeviceIp) return;
    const url = `http://${state.selectedDeviceIp}${normalized}`;
    const isApp = kind === 'app';
    const pane = q(isApp ? '#appEmbeddedPane' : '#serviceEmbeddedPane');
    const frame = q(isApp ? '#embeddedAppFrame' : '#embeddedServiceFrame');
    const titleNode = q(isApp ? '#embeddedAppTitle' : '#embeddedServiceTitle');
    const urlNode = q(isApp ? '#embeddedAppUrl' : '#embeddedServiceUrl');
    titleNode.textContent = title || (isApp ? '应用控制' : '服务设置');
    urlNode.textContent = url;
    pane.classList.remove('empty');
    const mustReload = frame.dataset.url !== url || forceReload || (isApp && state.forceAppFrameReload);
    if (mustReload) {
      const target = new URL(url);
      target.searchParams.set('_cubic_reload', String(Date.now()));
      frame.dataset.url = url;
      frame.src = target.href;
      if (isApp) state.forceAppFrameReload = false;
    }
    if (isApp) q('#configureCurrentMonitor').classList.toggle('hidden', !/^holo_pc_monitor$/i.test(appId));
  }

  function clearEmbeddedDevicePage(kind) {
    const isApp = kind === 'app';
    const pane = q(isApp ? '#appEmbeddedPane' : '#serviceEmbeddedPane');
    const frame = q(isApp ? '#embeddedAppFrame' : '#embeddedServiceFrame');
    pane.classList.add('empty');
    if (frame.dataset.url) { frame.removeAttribute('src'); frame.dataset.url = ''; }
    if (isApp) {
      q('#configureCurrentMonitor').classList.add('hidden');
      q('#embeddedAppPlaceholder b').textContent = I18n.t('应用控制页面');
      q('#embeddedAppPlaceholder p').textContent = I18n.t('打开应用后，如果应用提供控制页，将在这里与应用列表左右分屏显示。');
    }
  }

  function showNoAppControlPage(title, appId) {
    clearEmbeddedDevicePage('app');
    q('#embeddedAppTitle').textContent = title || appId || I18n.t('应用控制');
    q('#embeddedAppUrl').textContent = appId || '';
    q('#embeddedAppPlaceholder b').textContent = I18n.t('该应用无控制页');
    q('#embeddedAppPlaceholder p').textContent = I18n.t('应用正在设备上运行，但没有提供 Web 控制页面。');
    state.forceAppFrameReload = false;
  }

  function renderDeviceSettings(settings, display, schedule, deviceState) {
    const deviceLanguage = I18n.normalize(settings.language || settings.locale || settings.lang || 'zh-CN');
    if (deviceLanguage !== I18n.language) syncLanguageToDevice();
    q('#weatherAddress').value = settings.weather_address || settings.weatherAddress || '';
    ensureOption(q('#timezoneSelect'), settings.timezone || 'CST-8');
    q('#timezoneSelect').value = settings.timezone || 'CST-8';
    const brightness = Number(display?.brightness ?? settings.brightness ?? settings.display_brightness ?? 80);
    q('#brightnessRange').value = Math.max(1, Math.min(100, brightness));
    q('#brightnessValue').textContent = `${q('#brightnessRange').value}%`;
    const sleepEnabled = display?.auto_sleep_enabled ?? settings.auto_sleep_enabled ?? false;
    const seconds = Number(display?.auto_sleep_seconds ?? settings.auto_sleep_seconds ?? 1800);
    const select = q('#autoSleepSelect');
    ensureOption(select, sleepEnabled ? String(seconds) : '0', sleepEnabled ? `${Math.round(seconds / 60)} 分钟` : '关闭');
    select.value = sleepEnabled ? String(seconds) : '0';

    const scheduleData = { ...settings, ...(schedule || {}) };
    q('#scheduledSleepEnabled').value = String(Boolean(scheduleData.scheduled_sleep_enabled));
    q('#scheduledSleepMode').value = scheduleData.scheduled_sleep_mode === 'dim' ? 'dim' : 'off';
    q('#scheduledSleepTime').value = timeValue(scheduleData.scheduled_sleep_hour, scheduleData.scheduled_sleep_minute, '00:00');
    q('#scheduledWakeTime').value = timeValue(scheduleData.scheduled_wake_hour, scheduleData.scheduled_wake_minute, '07:00');

    const soundSelect = q('#alarmSound');
    const files = Array.isArray(scheduleData.mp3_files) ? scheduleData.mp3_files : [];
    soundSelect.innerHTML = '<option value="">默认嘀嘀声</option>' + files.map(file => `<option value="${escapeHtml(file.path || '')}">${escapeHtml(file.name || file.path || '')}</option>`).join('');
    ensureOption(soundSelect, scheduleData.alarm_sound || '', scheduleData.alarm_sound || '默认嘀嘀声');
    soundSelect.value = scheduleData.alarm_sound || '';
    renderAlarmRows(Array.isArray(scheduleData.alarms) ? scheduleData.alarms : []);
    renderFirmwareUpdate(deviceState?.firmware_update || {});
  }

  function renderFirmwareUpdate(firmware) {
    const phase = String(firmware?.phase || 'idle');
    const active = Boolean(firmware?.active);
    const available = Boolean(firmware?.update_available);
    const percent = Math.max(0, Math.min(100, Number(firmware?.percent || 0)));
    const labels = { check:'检查中', download:'下载中', install:'安装中', rebooting:'即将重启', error:'更新失败' };
    const badge = labels[phase] || (available ? '有新版本' : '已是最新');
    q('#firmwareCurrentVersion').textContent = firmware?.current_version || '-';
    q('#firmwareLatestVersion').textContent = firmware?.latest_version || '-';
    q('#firmwareBadge').textContent = I18n.t(badge);
    q('#firmwareBadge').className = `status-tag ${phase === 'error' ? 'error' : active || available ? 'running' : ''}`;
    let note = '当前固件无需更新。';
    if (phase === 'error') note = I18n.format('更新失败：{0}', firmware?.error || I18n.t('请稍后重试'));
    else if (phase === 'rebooting') note = '固件已写入，设备正在重启。';
    else if (active && phase === 'download') note = I18n.format('正在下载 {0}%', percent);
    else if (active) note = badge;
    else if (available) note = firmware?.notes || '发现可安装的新固件。';
    q('#firmwareNote').textContent = I18n.t(note);
    q('#firmwareProgressFill').style.width = `${active || phase === 'rebooting' ? percent : 0}%`;
    q('#checkFirmware').disabled = active;
    q('#checkFirmware').textContent = I18n.t(phase === 'check' ? '检查中...' : '检查更新');
    q('#installFirmware').disabled = active || !available;
    q('#installFirmware').textContent = I18n.t(phase === 'rebooting' ? '即将重启' : active ? '安装中...' : '安装更新');
    clearTimeout(state.firmwarePollTimer);
    state.firmwarePollTimer = active ? setTimeout(() => post('device.control.refresh'), 1600) : null;
  }

  function timeValue(hour, minute, fallback) {
    const h = Number(hour), m = Number(minute);
    return Number.isFinite(h) && Number.isFinite(m) ? `${String(Math.max(0, Math.min(23, h))).padStart(2, '0')}:${String(Math.max(0, Math.min(59, m))).padStart(2, '0')}` : fallback;
  }

  function parseTimeValue(value, fallbackHour = 0) {
    const match = String(value || '').match(/^(\d{1,2}):(\d{2})$/);
    return match ? { hour: Math.max(0, Math.min(23, Number(match[1]))), minute: Math.max(0, Math.min(59, Number(match[2]))) } : { hour: fallbackHour, minute: 0 };
  }

  function renderAlarmRows(alarms) {
    const repeats = [['daily','每日'],['weekdays','工作日'],['weekend','周末'],['mon','每周一'],['tue','每周二'],['wed','每周三'],['thu','每周四'],['fri','每周五'],['sat','每周六'],['sun','每周日']];
    const normalized = Array.from({ length: 3 }, (_, index) => alarms[index] || { enabled:false, hour:7, minute:0, repeat:'daily' });
    q('#alarmRows').innerHTML = normalized.map((alarm, index) => `<div class="alarm-row" data-index="${index}"><label class="alarm-enable"><input class="alarm-enabled" type="checkbox" ${alarm.enabled ? 'checked' : ''}><span>闹钟 ${index + 1}</span></label><label><span>时间</span><input class="alarm-time" type="time" value="${timeValue(alarm.hour, alarm.minute, '07:00')}"></label><label><span>重复</span><select class="alarm-repeat">${repeats.map(([value,label]) => `<option value="${value}" ${alarm.repeat === value ? 'selected' : ''}>${label}</option>`).join('')}</select></label></div>`).join('');
    I18n.localize(q('#alarmRows'));
  }

  function readAlarmRows() {
    return qa('#alarmRows .alarm-row').map(row => {
      const time = parseTimeValue(row.querySelector('.alarm-time').value, 7);
      return { enabled: row.querySelector('.alarm-enabled').checked, hour: time.hour, minute: time.minute, repeat: row.querySelector('.alarm-repeat').value };
    });
  }

  function renderDeviceServices(deviceState) {
    const services = installedItems(deviceState).filter(item => String(item.kind || '').toLowerCase() === 'service');
    q('#deviceServiceGrid').innerHTML = services.length ? services.map(service => {
      const route = serviceRoute(service);
      const storeItem = state.catalog.find(item => item.id === service.id);
      return `<article class="panel control-card device-service-row"><div class="control-card-head">${appIconMarkup(storeItem || service)}<div><h3>${escapeHtml(service.name || service.id)}</h3><p>${escapeHtml(service.id || '')} · ${escapeHtml(service.version || I18n.t('未知版本'))}</p></div></div><div class="button-row">${route ? `<button class="button primary embed-service-path" data-path="${escapeHtml(route)}" data-title="${escapeHtml(service.name || service.id)}">打开</button>` : '<span class="service-no-controls">无控制页</span>'}</div></article>`;
    }).join('') : '<div class="panel empty-state">设备没有返回服务列表。</div>';
    qa('#deviceServiceGrid .embed-service-path').forEach(button => button.addEventListener('click', () => showEmbeddedDevicePage('service', button.dataset.path, button.dataset.title)));

    const alarm = services.find(service => /display|alarm/i.test(`${service.id} ${service.name}`));
    q('#openAlarmService').dataset.path = alarm ? serviceRoute(alarm) : '/display-schedule/';
    I18n.localize(q('#deviceServiceGrid'));
  }

  function serviceRoute(service) {
    const route = String(service?.route_base || '').trim();
    if (String(service?.id || '').toLowerCase() === 'display_schedule') return '/display-schedule/';
    return route ? normalizeRoute(route) : service?.id ? `/${encodeURIComponent(service.id)}/` : '';
  }

  function normalizeRoute(route) {
    const value = String(route || '').trim();
    if (!value || value.startsWith('//') || /^[a-z]+:/i.test(value)) return '';
    const withSlash = value.startsWith('/') ? value : `/${value}`;
    return withSlash.endsWith('/') ? withSlash : `${withSlash}/`;
  }

  function renderStoreStatus(result) {
    if (result.status !== 'working') state.catalogLoading = false;
    q('#storeStatus').textContent = result.message || (result.status === 'working' ? '正在读取应用商店…' : '应用商店操作完成');
    q('#storeStatus').className = `store-status ${result.status === 'error' ? 'error-text' : ''}`;
    if (result.status === 'success') toast(result.message || '应用商店操作完成');
  }

  function renderStore(payload) {
    const catalog = payload.catalog || {};
    const installed = installedStoreMap();
    const catalogItems = (Array.isArray(catalog.items) ? catalog.items : [])
      .map(item => {
        const id = String(item.id || item.app_id || '');
        const installedInfo = installed.get(id) || null;
        return { ...item, id, kind:storeItemKind(item, installedInfo), installedInfo };
      })
      .filter(item => item.id);
    const seen = new Set(catalogItems.map(item => item.id));
    const localItems = [...installed.values()].filter(item => !seen.has(String(item.id || ''))).map(item => ({
      ...item,
      id:String(item.id || ''),
      kind:storeItemKind(item, item),
      installedInfo:item,
      description:item.description || '设备已安装，本次目录未返回该应用。',
      channel:'local'
    }));
    state.catalog = catalogItems.concat(localItems);
    state.catalogDeviceIp = payload.ip || state.selectedDeviceIp;
    state.catalogLoading = false;
    renderStoreGrid();
    if (state.control?.state) {
      renderDeviceApps(state.control.state);
      renderDeviceServices(state.control.state);
    }
  }

  function storeItemKind(item, installedInfo = null) {
    const raw = String(item?.kind || item?.app_kind || item?.type || installedInfo?.kind || '').trim().toLowerCase();
    const id = String(item?.id || item?.app_id || installedInfo?.id || '').trim().toLowerCase();
    if (raw === 'launcher' || id === 'launcher') return 'launcher';
    if (raw === 'service' || id.endsWith('-service') || id.endsWith('_service') || ['devtools','hidpad','display_schedule','holocubic-lua-claw'].includes(id)) return 'service';
    return 'app';
  }

  function installedStoreMap() {
    const deviceState = state.control?.state || {};
    const installed = new Map(installedItems(deviceState).map(item => [String(item.id || ''), item]));
    if (deviceState.main_path || deviceState.current_route_base || deviceState.ok) {
      installed.set('launcher', installed.get('launcher') || { id:'launcher', name:'Launcher', kind:'launcher', source:'system' });
    }
    return installed;
  }

  function storeSearchText(item) {
    return `${item?.id || ''} ${item?.name || ''} ${item?.name_zh_cn || ''} ${item?.name_en || ''}`.toLowerCase();
  }

  function matchesStoreFilter(item, installed, filter) {
    if (filter === 'installed') return installed.has(item.id);
    if (filter === 'available') return !installed.has(item.id);
    if (filter === 'service') return storeItemKind(item, installed.get(item.id)) === 'service';
    if (filter === 'launcher') return storeItemKind(item, installed.get(item.id)) === 'launcher' || /launcher|启动器/.test(storeSearchText(item));
    if (filter === 'weather') return /weather|天气/.test(storeSearchText(item));
    if (filter === 'time') return /time|clock|时间|时钟/.test(storeSearchText(item));
    return true;
  }

  function storeInstallJobs(deviceState = state.control?.state || {}) {
    const install = deviceState?.app_install;
    if (!install) return [];
    if (Array.isArray(install.jobs)) return install.jobs.filter(Boolean);
    return Number(install.seq || 0) > 0 ? [install] : [];
  }

  function maxStoreInstallSeq() {
    return storeInstallJobs().reduce((max, job) => Math.max(max, Number(job?.seq || 0)), 0);
  }

  function currentStoreProgress() {
    const progress = new Map();
    const jobs = storeInstallJobs();
    jobs.forEach(job => {
      const appId = String(job?.app_id || '').trim();
      const phase = String(job?.phase || '').trim().toLowerCase();
      if (!appId || !phase || phase === 'idle' || phase === 'done' || phase === 'error') return;
      progress.set(appId, { percent:Math.max(0, Math.min(100, Number(job?.percent || 0))), phase });
    });
    for (const [appId, baseline] of state.storePendingInstalls) {
      const newer = jobs.filter(job => String(job?.app_id || '') === appId && Number(job?.seq || 0) > baseline);
      if (newer.some(job => ['done','error'].includes(String(job?.phase || '').toLowerCase()))) {
        state.storePendingInstalls.delete(appId);
      } else if (!progress.has(appId)) {
        progress.set(appId, { percent:0, phase:'queued' });
      }
    }
    for (const [appId, task] of state.pcStoreProgress) progress.set(appId, task);
    return progress;
  }

  function compareStoreVersions(left, right) {
    const a = String(left || '').trim();
    const b = String(right || '').trim();
    if (!a || !b) return 0;
    if (/^\d+(?:[._-]\d+)*$/.test(a) && /^\d+(?:[._-]\d+)*$/.test(b)) {
      const aa = a.split(/[._-]/).map(Number);
      const bb = b.split(/[._-]/).map(Number);
      const length = Math.max(aa.length, bb.length);
      for (let index = 0; index < length; index += 1) {
        const delta = (aa[index] || 0) - (bb[index] || 0);
        if (delta) return delta > 0 ? 1 : -1;
      }
      return 0;
    }
    return a.localeCompare(b, undefined, { numeric:true, sensitivity:'base' });
  }

  function storeUpdateAvailable(item, installed) {
    if (!installed) return false;
    const latest = item?.version || item?.latest_version;
    return Boolean(latest && installed.version && compareStoreVersions(latest, installed.version) > 0);
  }

  function renderPcStoreProgress(payload) {
    const appId = String(payload?.appId || payload?.app_id || '').trim();
    if (!appId) return;
    const status = String(payload?.status || 'working').toLowerCase();
    if (status === 'success' || status === 'error') {
      state.pcStoreProgress.delete(appId);
      if (payload?.message) toast(I18n.t(payload.message), status === 'error');
      if (status === 'success' && String(payload?.phase || '').toLowerCase() === 'installed') post('device.control.refresh', { ip:state.selectedDeviceIp });
    } else {
      state.pcStoreProgress.set(appId, {
        percent:Math.max(0, Math.min(100, Number(payload?.percent || 0))),
        phase:String(payload?.phase || 'download'),
        completed:Math.max(0, Number(payload?.completed || 0)),
        total:Math.max(0, Number(payload?.total || 0)),
        message:String(payload?.message || ''),
        mode:'pc',
      });
    }
    renderStoreGrid();
  }

  function renderPcStoreCache(payload) {
    state.pcStoreCached = new Map();
    (Array.isArray(payload?.packages) ? payload.packages : []).forEach(item => {
      const appId = String(item?.appId || item?.app_id || '');
      const current = state.pcStoreCached.get(appId);
      if (appId && (!current || compareStoreVersions(item?.version, current?.version) > 0)) state.pcStoreCached.set(appId, item);
    });
    renderStoreGrid();
  }

  function formatTransferBytes(value) {
    const bytes = Math.max(0, Number(value || 0));
    if (bytes >= 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(bytes >= 10 * 1024 * 1024 ? 1 : 2)} MB`;
    if (bytes >= 1024) return `${(bytes / 1024).toFixed(bytes >= 10 * 1024 ? 0 : 1)} KB`;
    return `${Math.round(bytes)} B`;
  }

  function scheduleStoreProgressPoll(hasWork) {
    clearTimeout(state.storePollTimer);
    state.storePollTimer = hasWork ? setTimeout(() => post('device.control.refresh'), 1200) : null;
  }

  function renderStoreGrid() {
    const host = q('#storeGrid');
    if (!host) return;
    const installed = installedStoreMap();
    const progress = currentStoreProgress();
    const installMode = q('#storeInstallMode')?.value === 'pc' ? 'pc' : 'device';
    const visibleItems = state.catalog.filter(item => matchesStoreFilter(item, installed, state.currentStoreFilter));
    const filterLabels = { all:'全部', installed:'已安装', available:'可安装', service:'服务', launcher:'启动器', weather:'天气', time:'时间' };
    q('#storeStatus').textContent = state.currentStoreFilter === 'all'
      ? I18n.format('已读取 {0} 个应用', state.catalog.length)
      : `${I18n.t(filterLabels[state.currentStoreFilter] || '全部')} · ${visibleItems.length} / ${state.catalog.length}`;
    host.innerHTML = visibleItems.length ? visibleItems.map(item => {
      const current = installed.get(item.id);
      const manifest = item.manifest_url || item.manifestUrl || '';
      const pageUrl = storePageWithLanguage(item.description_page_url || item.descriptionPageUrl);
      const descriptionButton = pageUrl ? `<button class="button secondary store-detail-button" data-url="${escapeHtml(pageUrl)}">查看介绍</button>` : '';
      const task = progress.get(item.id);
      const cached = state.pcStoreCached.get(item.id);
      const cachedForVersion = Boolean(cached && String(cached.version || '') === String(item.version || item.latest_version || ''));
      const canUpdate = storeUpdateAvailable(item, current);
      const canInstall = !current || canUpdate;
      const actionText = installMode === 'pc' ? I18n.t(cachedForVersion ? '安装到设备' : '下载到电脑') : I18n.t(current ? '更新' : '安装');
      const taskLabel = I18n.t(task?.mode === 'pc' ? (String(task.phase).toLowerCase() === 'install' ? '安装中' : '下载中') : (current ? '更新中' : '安装中'));
      const taskText = task ? `${taskLabel} ${Math.round(task.percent)}%` : '';
      const taskBytes = task?.mode === 'pc' && task.total > 0 ? `${formatTransferBytes(task.completed)} / ${formatTransferBytes(task.total)}` : '';
      const progressValue = taskBytes ? `${taskBytes} · ${Math.round(task.percent)}%` : `${Math.round(task?.percent || 0)}%`;
      const progressMarkup = task ? `<div class="store-install-progress" aria-label="${escapeHtml(taskText)}"><div><span>${taskLabel}</span><b>${escapeHtml(progressValue)}</b></div><div class="store-progress-track"><i style="width:${task.percent}%"></i></div></div>` : '';
      const pcAction = cachedForVersion ? 'install' : 'download';
      const installButton = canInstall && (installMode === 'pc' || manifest) ? `<button class="button primary store-install" data-id="${escapeHtml(item.id)}" data-name="${escapeHtml(item.name || item.id)}" data-version="${escapeHtml(item.version || item.latest_version || '')}" data-manifest="${escapeHtml(manifest)}" data-pc-action="${pcAction}" ${task ? 'disabled' : ''}>${task ? taskText : actionText}</button>` : '';
      const actionButtons = `${descriptionButton}${installButton}${current && item.id !== 'launcher' && !task ? `<button class="button danger store-uninstall" data-id="${escapeHtml(item.id)}">卸载</button>` : ''}`;
      return `<article class="panel store-card" data-id="${escapeHtml(item.id)}"><div class="store-card-main">${appIconMarkup(item)}<div class="store-card-copy"><div class="store-card-title"><h3>${escapeHtml(item.name || item.id)}</h3>${current ? '<span class="status-tag running store-installed-tag">已安装</span>' : ''}</div><p class="store-card-meta">${escapeHtml(item.id || '')} · ${escapeHtml(item.version || item.latest_version || I18n.t('未知版本'))}</p><p class="store-card-description">${escapeHtml(item.description || '暂无应用介绍')}</p></div></div>${progressMarkup}${actionButtons ? `<div class="store-card-footer"><div class="store-card-actions">${actionButtons}</div></div>` : ''}</article>`;
    }).join('') : '<div class="panel empty-state">当前分类没有可显示的应用。</div>';
    qa('.store-detail-button').forEach(button => button.addEventListener('click', () => post('device.store.description.open', { url:button.dataset.url })));
    qa('.store-install').forEach(button => button.addEventListener('click', () => {
      const mode = q('#storeInstallMode')?.value === 'pc' ? 'pc' : 'device';
      if (mode === 'pc') {
        const phase = button.dataset.pcAction === 'install' ? 'install' : 'download';
        state.pcStoreProgress.set(button.dataset.id, { percent:0, completed:0, total:0, phase, mode:'pc' });
        renderStoreGrid();
        post(phase === 'install' ? 'device.store.pc.install' : 'device.store.pc.download', { id:button.dataset.id, version:button.dataset.version, transport:q('#storeTransferMode')?.value === 'devtools' ? 'devtools' : 'fs' });
        return;
      }
      state.storePendingInstalls.set(button.dataset.id, maxStoreInstallSeq());
      renderStoreGrid();
      post('device.store.install', { id:button.dataset.id, name:button.dataset.name, version:button.dataset.version, manifestUrl:button.dataset.manifest });
    }));
    qa('.store-uninstall').forEach(button => button.addEventListener('click', () => post('device.store.uninstall', { id: button.dataset.id })));
    bindIconFallbacks(host);
    I18n.localize(host);
    scheduleStoreProgressPoll(state.storePendingInstalls.size > 0);
  }

  function safeStorePageUrl(value) {
    try {
      const url = new URL(String(value || ''));
      return url.protocol === 'https:' && url.hostname.toLowerCase() === 'cubic.clocteck.com' ? url.href : '';
    } catch { return ''; }
  }

  function storePageWithLanguage(value) {
    const safe = safeStorePageUrl(value);
    if (!safe) return '';
    const url = new URL(safe);
    url.searchParams.set('lang', I18n.language);
    return url.href;
  }

  function safeIconUrl(value) {
    const raw = String(value || '').trim();
    if (raw.startsWith('/') && !raw.startsWith('//') && state.selectedDeviceIp) return `http://${state.selectedDeviceIp}${raw}`;
    if (!/^https?:\/\//i.test(raw)) return '';
    try { return new URL(raw).href; } catch { return ''; }
  }

  function appIconMarkup(item) {
    const letter = escapeHtml((item?.name || item?.id || 'A').slice(0, 1));
    const iconUrl = safeIconUrl(item?.icon_url || item?.iconUrl);
    return `<div class="app-letter app-store-icon"><span>${letter}</span>${iconUrl ? `<img class="app-icon-image" src="${escapeHtml(iconUrl)}" alt="" loading="lazy" referrerpolicy="no-referrer">` : ''}</div>`;
  }

  function bindIconFallbacks(root) {
    if (!root) return;
    root.querySelectorAll('.app-icon-image').forEach(image => image.addEventListener('error', () => image.remove(), { once:true }));
  }

  function renderMonitorConfig(result) {
    if (!result) return;
    const isError = result.status === 'error';
    toast(result.message || (isError ? '自动配置失败' : '全息 PC 监控已配置'), isError);
  }

  function renderHolopetConfig(result) {
    if (!result) return;
    toast(result.message || (result.status === 'error' ? 'Holopet 自动配置失败' : 'Holopet 已自动配置'), result.status === 'error');
  }

  function renderSmtcMusicConfig(result) {
    if (!result) return;
    toast(result.message || (result.status === 'error' ? 'SMTC Music 自动配置失败' : 'SMTC Music 已自动配置'), result.status === 'error');
  }

  function renderServices(services) {
    state.services = services;
    const statusName = { running:'运行中', external:'外部运行', stopped:'已停止', error:'错误', unconfigured:'未配置' };
    const statusClass = value => value === 'running' ? 'running' : value === 'error' ? 'error' : '';
    const running = services.filter(service => service.status === 'running' || service.status === 'external');
    const host = q('#serviceSummary');
    if (!host) return;
    host.innerHTML = running.length ? running.map(service => `<article class="summary-card running-service-card"><div class="running-service-main"><div class="service-icon">${escapeHtml(service.name.slice(0,2))}</div><div><div class="row"><h4>${escapeHtml(service.name)}</h4><span class="status-tag ${statusClass(service.status)}">${statusName[service.status] || service.status}</span></div><p>${escapeHtml(service.description)}</p><small>${I18n.t('监听端口')} ${service.port || '--'} · ${escapeHtml(service.message)}</small></div></div><button class="button danger overview-stop-service" data-id="${escapeHtml(service.id)}" ${service.status === 'external' ? 'disabled title="服务由外部程序启动"' : ''}>停止服务</button></article>`).join('') : '<div class="panel empty-state service-empty">当前没有运行中的电脑服务。打开 Holopet、电脑性能监控等应用后会自动启动对应服务。</div>';
    qa('#serviceSummary .overview-stop-service').forEach(button => button.addEventListener('click', () => post('services.stop', { id:button.dataset.id })));
    I18n.localize(host);
  }

  function appendLog(entry) { if (!entry) return; state.logs.push(entry); if (state.logs.length > 600) state.logs.splice(0,100); renderLogs(); }
  function renderLogs() {
    const box = q('#logList');
    if (!state.logs.length) { box.innerHTML = '<div class="log-empty">等待运行日志…</div>'; return; }
    box.innerHTML = state.logs.slice(-400).map(entry => { const time = new Date(entry.time).toLocaleTimeString('zh-CN',{hour12:false}); return `<div class="log-row ${escapeHtml(entry.level)}"><time>${time}</time><b>${escapeHtml(entry.source)}</b><p>${escapeHtml(entry.message)}</p></div>`; }).join('');
    box.scrollTop = box.scrollHeight;
  }

  function renderSerialStatus(snapshot) {
    state.serial = snapshot || {};
    const ports = Array.isArray(snapshot?.ports) ? snapshot.ports : [];
    const select = q('#serialPortSelect');
    const preferred = snapshot?.connectedPort || select.value || ports[0] || '';
    select.innerHTML = ports.length
      ? ports.map(port => `<option value="${escapeHtml(port)}">${escapeHtml(port)}</option>`).join('')
      : '<option value="">未发现串口</option>';
    select.value = ports.includes(preferred) ? preferred : (ports[0] || '');
    if (snapshot?.baudRate) q('#serialBaudSelect').value = String(snapshot.baudRate);
    const connected = Boolean(snapshot?.connected);
    select.disabled = connected;
    q('#serialBaudSelect').disabled = connected;
    q('#connectSerial').classList.toggle('hidden', connected);
    q('#disconnectSerial').classList.toggle('hidden', !connected);
    q('#serialStatus').textContent = snapshot?.error
      ? snapshot.error
      : connected ? `${snapshot.connectedPort} @ ${snapshot.baudRate}` : I18n.t('串口未连接');
    q('#serialStatus').className = `serial-status ${connected ? 'connected' : snapshot?.error ? 'error-text' : ''}`;
    q('#serialPortMetric').textContent = snapshot?.connectedPort || preferred || '--';
    q('#serialBaudMetric').textContent = String(snapshot?.baudRate || q('#serialBaudSelect').value || 115200);
    q('#serialBytesMetric').textContent = formatFsBytes(Number(snapshot?.receivedBytes || 0));
    q('#serialConnectedMetric').textContent = connected && snapshot?.connectedAt
      ? new Date(snapshot.connectedAt).toLocaleTimeString(undefined, { hour12:false })
      : '--';
    q('#serialErrorMetric').textContent = snapshot?.error || I18n.t(connected ? '已连接' : '未连接');
    q('#serialToolStatusBadge').textContent = snapshot?.error
      ? I18n.t('串口错误')
      : I18n.t(connected ? '已连接' : '串口未连接');
    q('#serialToolStatusBadge').className = `status-tag ${connected ? 'running' : snapshot?.error ? 'error' : ''}`;
    I18n.localize(select);
  }

  function appendSerialText(chunk) {
    const text = String(chunk?.text || '');
    if (!text) return;
    state.serialText = (state.serialText + text).slice(-240000);
    if (Number.isFinite(Number(chunk?.receivedBytes))) q('#serialBytesMetric').textContent = formatFsBytes(Number(chunk.receivedBytes));
    const output = q('#serialOutput');
    output.textContent = state.serialText;
    output.scrollTop = output.scrollHeight;
  }

  function renderDeviceFsStatus(payload) {
    const node = q('#deviceFsStatus');
    const key = payload?.messageKey || payload?.message || '';
    const args = Array.isArray(payload?.args) ? payload.args : [];
    const message = args.length ? I18n.format(key, ...args) : I18n.t(key);
    node.textContent = message;
    node.className = payload?.status === 'error' ? 'error-text' : payload?.status === 'working' ? 'working' : '';
    if (payload?.status === 'success' && message) toast(message);
  }

  function deviceFsItems(payload) {
    const result = payload?.result || payload || {};
    const raw = Array.isArray(result) ? result : Array.isArray(result.entries) ? result.entries : Array.isArray(result.items) ? result.items : [];
    const base = payload?.path || result.path || state.fs.path || '/sd';
    return raw.map(item => {
      const name = String(item?.name || '').trim();
      const path = String(item?.path || `${base.replace(/\/$/, '')}/${name}`);
      const isDir = Boolean(item?.is_dir ?? item?.isDir ?? (item?.type === 'dir'));
      return { ...item, name, path, isDir, size:Number(item?.size || 0) };
    }).filter(item => item.name && item.path).sort((a,b) => a.isDir === b.isDir ? a.name.localeCompare(b.name) : a.isDir ? -1 : 1);
  }

  function renderDeviceFiles(payload) {
    const path = payload?.path || payload?.result?.path || '/sd';
    state.fs = { deviceIp:payload?.ip || state.selectedDeviceIp, path, items:deviceFsItems(payload), selected:null, previewText:'' };
    q('#deviceFilesPath').value = path;
    const presets = qa('[data-media-path]');
    const activePreset = presets.filter(button => path === button.dataset.mediaPath || path.startsWith(`${button.dataset.mediaPath}/`)).sort((a,b) => b.dataset.mediaPath.length - a.dataset.mediaPath.length)[0];
    presets.forEach(button => button.classList.toggle('active', button === activePreset));
    if (activePreset) state.mediaKind = activePreset.dataset.mediaKind || 'app';
    q('#deviceFilesSummary').textContent = I18n.format('{0} 个项目', state.fs.items.length);
    q('#deviceFsStatus').textContent = I18n.t('目录已刷新');
    clearDeviceFileSelection();
    const host = q('#deviceFileList');
    if (!state.fs.items.length) {
      host.innerHTML = '<div class="file-empty">当前目录为空。</div>';
      I18n.localize(host);
      return;
    }
    host.innerHTML = state.fs.items.map(item => {
      const ext = item.isDir ? 'DIR' : (item.name.split('.').pop() || 'FILE').slice(0,4).toUpperCase();
      return `<button class="device-file-row" type="button" data-path="${escapeHtml(item.path)}"><span class="device-file-icon ${item.isDir ? 'directory' : ''}">${escapeHtml(ext)}</span><span class="device-file-copy"><b>${escapeHtml(item.name)}</b><small>${item.isDir ? I18n.t('目录') : formatFsBytes(item.size)}</small></span><span class="device-file-open">${I18n.t(item.isDir ? '打开' : '预览')}</span></button>`;
    }).join('');
    qa('#deviceFileList .device-file-row').forEach(row => row.addEventListener('click', () => {
      const item = state.fs.items.find(entry => entry.path === row.dataset.path);
      if (!item) return;
      selectDeviceFile(item);
    }));
    qa('#deviceFileList .device-file-row').forEach(row => row.addEventListener('dblclick', () => {
      const item = state.fs.items.find(entry => entry.path === row.dataset.path);
      if (item?.isDir) post('device.fs.list', { path:item.path });
    }));
    updateFileClipboardState();
  }

  function selectDeviceFile(item) {
    state.fs.selected = item;
    qa('#deviceFileList .device-file-row').forEach(row => row.classList.toggle('selected', row.dataset.path === item.path));
    q('#selectedDeviceFileName').textContent = item.name;
    q('#selectedDeviceFilePath').textContent = item.isDir ? `${item.path} · ${I18n.t('目录')}` : `${item.path} · ${formatFsBytes(item.size)}`;
    q('#downloadDeviceFile').disabled = item.isDir;
    q('#deleteDeviceFile').disabled = false;
    q('#renameDeviceFile').disabled = false;
    q('#copyDeviceFile').disabled = false;
    q('#cutDeviceFile').disabled = false;
    q('#editLuaFile').classList.add('hidden');
    state.fs.previewText = '';
    if (item.isDir) {
      q('#deviceFilePreview').innerHTML = `<div class="file-empty">${escapeHtml(I18n.t('文件夹已选择，双击左侧项目可打开。'))}</div>`;
    } else {
      q('#deviceFilePreview').innerHTML = '<div class="file-empty">正在读取预览…</div>';
      post('device.fs.preview', { path:item.path });
    }
  }

  function clearDeviceFileSelection() {
    state.fs.selected = null;
    q('#selectedDeviceFileName').textContent = I18n.t('未选择文件');
    q('#selectedDeviceFilePath').textContent = I18n.t('从左侧选择图片或文件');
    q('#downloadDeviceFile').disabled = true;
    q('#deleteDeviceFile').disabled = true;
    q('#renameDeviceFile').disabled = true;
    q('#copyDeviceFile').disabled = true;
    q('#cutDeviceFile').disabled = true;
    q('#editLuaFile').classList.add('hidden');
    state.fs.previewText = '';
    q('#deviceFilePreview').innerHTML = '<div class="file-empty">支持预览 PNG、JPG、GIF、WebP、Lua、JSON、歌词和文本文件。</div>';
    I18n.localize(q('#deviceFilePreview'));
  }

  function renderDeviceFilePreview(payload) {
    if (!state.fs.selected || payload?.path !== state.fs.selected.path) return;
    const host = q('#deviceFilePreview');
    if (payload.kind === 'image' && /^data:image\//.test(payload.content || '')) {
      host.innerHTML = `<img src="${escapeHtml(payload.content)}" alt="${escapeHtml(state.fs.selected.name)}">`;
    } else if (payload.kind === 'text') {
      state.fs.previewText = String(payload.content || '');
      host.innerHTML = `<pre>${escapeHtml(payload.content || '')}</pre>`;
      q('#editLuaFile').classList.toggle('hidden', !/\.lua$/i.test(state.fs.selected.name || ''));
    } else {
      host.innerHTML = `<div class="file-empty">${escapeHtml(I18n.t('该文件不支持内置预览，可下载到电脑查看。'))}</div>`;
    }
  }

  function parentDevicePath(path) {
    const parts = String(path || '/sd').split('/').filter(Boolean);
    if (parts.length <= 1) return '/sd';
    parts.pop();
    return `/${parts.join('/')}`;
  }

  function joinDevicePath(parent, name) {
    return `${String(parent || '/sd').replace(/\/$/, '')}/${String(name || '').replace(/^\/+/, '')}`;
  }

  function copiedName(name) {
    const dot = name.lastIndexOf('.');
    return dot > 0 ? `${name.slice(0,dot)} - 副本${name.slice(dot)}` : `${name} - 副本`;
  }

  function updateFileClipboardState() {
    const button = q('#pasteDeviceFile');
    button.disabled = !state.fsClipboard;
    button.title = state.fsClipboard ? `${state.fsClipboard.move ? I18n.t('剪切') : I18n.t('复制')} · ${state.fsClipboard.name}` : '';
  }

  function formatFsBytes(value) {
    const size = Number(value || 0);
    if (size >= 1024 * 1024) return `${(size / 1024 / 1024).toFixed(2)} MB`;
    if (size >= 1024) return `${(size / 1024).toFixed(1)} KB`;
    return `${size} B`;
  }

  function updateLuaMeta() {
    const editor = q('#luaCodeEditor');
    const lines = editor.value ? editor.value.split('\n').length : 1;
    q('#luaCodeMeta').textContent = `${lines} ${I18n.t('行')} · ${editor.value.length} ${I18n.t('字符')}`;
    q('#luaCodeState').textContent = I18n.t(editor.value === state.loadedLuaCode ? '未修改' : '已修改');
  }

  function renderLuaCode(payload) {
    q('#luaFilePath').value = payload?.path || '/sd/apps/devrun/main.lua';
    state.loadedLuaCode = String(payload?.code || '');
    q('#luaCodeEditor').value = state.loadedLuaCode;
    updateLuaMeta();
    toast(I18n.t('Lua 代码已读取'));
  }

  function renderLuaSaved(payload) {
    state.loadedLuaCode = q('#luaCodeEditor').value;
    updateLuaMeta();
    toast(I18n.t(payload?.run ? '已保存并运行 DevRun' : 'Lua 代码已保存'));
  }

  function ensureOption(select, value, label = value) {
    if (!value || [...select.options].some(option => option.value === value)) return;
    const option = document.createElement('option'); option.value = value; option.textContent = label; select.appendChild(option);
  }
  function syncLanguageToDevice(force = false) {
    if (!state.selectedDeviceIp) return;
    const key = `${state.selectedDeviceIp}:${I18n.language}`;
    if (!force && state.syncedLanguageKey === key) return;
    state.syncedLanguageKey = key;
    post('device.language.sync', { language:I18n.language });
  }
  function formatTime(value) { if (!value) return '--'; const date = new Date(value); return Number.isNaN(date.getTime()) ? '--' : date.toLocaleString('zh-CN', { hour12:false }); }

  let toastTimer;
  function toast(message, error = false) {
    const element = q('#toast'); element.textContent = message || ''; element.style.borderColor = error ? '#633438' : '#39443d'; element.classList.add('show'); clearTimeout(toastTimer); toastTimer = setTimeout(() => element.classList.remove('show'), 3800);
  }

  qa('.nav-item').forEach(button => button.addEventListener('click', () => gotoPage(button.dataset.page)));
  qa('[data-goto]').forEach(button => button.addEventListener('click', () => gotoPage(button.dataset.goto)));
  qa('[data-command]').forEach(button => button.addEventListener('click', () => post(button.dataset.command)));
  qa('[data-control-tab]').forEach(button => button.addEventListener('click', () => {
    gotoPage('control');
    state.currentControlTab = button.dataset.controlTab;
    qa('[data-control-page]').forEach(item => item.classList.remove('active'));
    qa('[data-control-tab]').forEach(item => item.classList.toggle('active', item === button));
    qa('.control-panel').forEach(panel => panel.classList.toggle('active', panel.id === `control-${state.currentControlTab}`));
  }));
  qa('[data-control-page]').forEach(button => button.addEventListener('click', () => gotoPage(button.dataset.controlPage)));
  qa('[data-store-filter]').forEach(button => button.addEventListener('click', () => {
    state.currentStoreFilter = button.dataset.storeFilter || 'all';
    qa('[data-store-filter]').forEach(item => {
      const active = item === button;
      item.classList.toggle('active', active);
      item.setAttribute('aria-pressed', active ? 'true' : 'false');
    });
    renderStoreGrid();
  }));
  q('#manualIpForm').addEventListener('submit', event => { event.preventDefault(); const ip = q('#manualIpInput').value.trim(); if (!ip) return toast('请输入设备 IP', true); post('device.connectIp', { ip }); });
  q('#headerDeviceSelect').addEventListener('change', event => { const ip = event.target.value; if (!ip) return; state.selectedDeviceIp = ip; post('device.select', { ip }); if (state.currentPage === 'control') post('device.control.refresh', { ip }); });
  q('#headerLanguageSelect').addEventListener('change', event => {
    const language = I18n.apply(event.target.value);
    event.target.value = language;
    state.syncedLanguageKey = '';
    syncLanguageToDevice(true);
    renderOverviewDevices();
    if (state.control?.state) {
      renderDeviceApps(state.control.state);
      renderDeviceServices(state.control.state);
    }
    if (state.catalog.length) renderStoreGrid();
    renderSerialStatus(state.serial || {});
    updateLuaMeta();
  });
  q('#loadStore').addEventListener('click', () => post('device.store.load'));
  q('#storeInstallMode').addEventListener('change', event => {
    q('#storeTransferModeField').classList.toggle('hidden', event.target.value !== 'pc');
    renderStoreGrid();
  });
  q('#exitCurrentApp').addEventListener('click', () => post('device.app.exit'));
  q('#brightnessRange').addEventListener('input', event => { q('#brightnessValue').textContent = `${event.target.value}%`; });
  q('#wakeDisplay').addEventListener('click', () => post('device.display.wake'));
  q('#openAlarmService').addEventListener('click', event => {
    state.currentControlTab = 'device-services';
    qa('[data-control-tab]').forEach(item => item.classList.toggle('active', item.dataset.controlTab === state.currentControlTab));
    qa('.control-panel').forEach(panel => panel.classList.toggle('active', panel.id === `control-${state.currentControlTab}`));
    showEmbeddedDevicePage('service', event.currentTarget.dataset.path || '/display-schedule/', '息屏与闹钟');
  });
  q('#testAlarm').addEventListener('click', () => post('device.alarm.test'));
  q('#stopAlarm').addEventListener('click', () => post('device.alarm.stop'));
  q('#checkFirmware').addEventListener('click', () => post('device.firmware.check'));
  q('#installFirmware').addEventListener('click', () => {
    if (window.confirm(I18n.t('安装固件更新后设备会自动重启。继续安装？'))) post('device.firmware.update');
  });
  q('#deviceSettingsForm').addEventListener('submit', event => {
    event.preventDefault();
    const seconds = Number(q('#autoSleepSelect').value || 0);
    const sleepTime = parseTimeValue(q('#scheduledSleepTime').value, 0);
    const wakeTime = parseTimeValue(q('#scheduledWakeTime').value, 7);
    q('#settingsSaveHint').textContent = '保存中…';
    const language = I18n.language;
    post('device.settings.save', {
      timezone:q('#timezoneSelect').value,
      weather_address:q('#weatherAddress').value.trim(),
      language,
      brightness:Number(q('#brightnessRange').value),
      auto_sleep_enabled:seconds > 0,
      auto_sleep_seconds:seconds > 0 ? seconds : 1800,
      scheduled_sleep_enabled:q('#scheduledSleepEnabled').value === 'true',
      scheduled_sleep_mode:q('#scheduledSleepMode').value,
      scheduled_sleep_hour:sleepTime.hour,
      scheduled_sleep_minute:sleepTime.minute,
      scheduled_wake_hour:wakeTime.hour,
      scheduled_wake_minute:wakeTime.minute,
      alarm_sound:q('#alarmSound').value,
      alarms:readAlarmRows()
    });
  });
  q('#clearLogs').addEventListener('click', () => { state.logs = []; renderLogs(); });
  q('#controlRefreshButton').addEventListener('click', () => { state.forceAppFrameReload = true; });
  qa('[data-log-view]').forEach(button => button.addEventListener('click', () => {
    state.currentLogView = button.dataset.logView || 'app';
    qa('[data-log-view]').forEach(item => item.classList.toggle('active', item === button));
    q('#appLogPanel').classList.toggle('active', state.currentLogView === 'app');
    q('#serialLogPanel').classList.toggle('active', state.currentLogView === 'serial');
    if (state.currentLogView === 'serial') post('serial.refresh');
  }));
  q('#refreshSerialPorts').addEventListener('click', () => post('serial.refresh'));
  q('#connectSerial').addEventListener('click', () => {
    const port = q('#serialPortSelect').value;
    if (!port) return toast(I18n.t('请先选择串口'), true);
    post('serial.connect', { port, baud:Number(q('#serialBaudSelect').value || 115200) });
  });
  q('#disconnectSerial').addEventListener('click', () => post('serial.disconnect'));
  q('#clearSerial').addEventListener('click', () => {
    state.serialText = '';
    q('#serialOutput').innerHTML = `<span>${I18n.t('等待串口输出…')}</span>`;
  });
  qa('[data-media-path]').forEach(button => button.addEventListener('click', () => {
    qa('[data-media-path]').forEach(item => item.classList.toggle('active', item === button));
    state.mediaKind = button.dataset.mediaKind || 'app';
    q('#deviceFilesPath').value = button.dataset.mediaPath;
    post('device.fs.list', { path:button.dataset.mediaPath });
  }));
  q('#refreshDeviceFiles').addEventListener('click', () => post('device.fs.list', { path:q('#deviceFilesPath').value.trim() || '/sd' }));
  q('#deviceFilesPath').addEventListener('keydown', event => { if (event.key === 'Enter') post('device.fs.list', { path:event.currentTarget.value.trim() || '/sd' }); });
  q('#deviceFilesUp').addEventListener('click', () => post('device.fs.list', { path:parentDevicePath(q('#deviceFilesPath').value) }));
  q('#createDeviceFolder').addEventListener('click', () => {
    const name = window.prompt(I18n.t('请输入新文件夹名称'), I18n.t('新建文件夹'))?.trim();
    if (!name) return;
    if (name === '.' || name === '..' || /[\\/]/.test(name)) return toast(I18n.t('名称不能包含斜杠'), true);
    const parent = q('#deviceFilesPath').value.trim() || '/sd';
    post('device.fs.mkdir', { path:joinDevicePath(parent, name), parent });
  });
  q('#uploadDeviceFiles').addEventListener('click', () => post('device.fs.upload.pick', { path:q('#deviceFilesPath').value.trim() || '/sd/images', mediaMode:q('#mediaResizeMode').value, mediaKind:state.mediaKind }));
  q('#downloadDeviceFile').addEventListener('click', () => {
    const item = state.fs.selected;
    if (item) post('device.fs.download', { path:item.path, name:item.name });
  });
  q('#deleteDeviceFile').addEventListener('click', () => {
    const item = state.fs.selected;
    if (!item || !window.confirm(I18n.format('确认删除 {0}？', item.name))) return;
    post('device.fs.delete', { path:item.path, parent:state.fs.path });
  });
  q('#renameDeviceFile').addEventListener('click', () => {
    const item = state.fs.selected;
    if (!item) return;
    const name = window.prompt(I18n.t('请输入新名称'), item.name)?.trim();
    if (!name || name === item.name) return;
    if (name === '.' || name === '..' || /[\\/]/.test(name)) return toast(I18n.t('名称不能包含斜杠'), true);
    post('device.fs.rename', { path:item.path, newPath:joinDevicePath(parentDevicePath(item.path), name), parent:state.fs.path });
  });
  q('#copyDeviceFile').addEventListener('click', () => {
    const item = state.fs.selected;
    if (!item) return;
    state.fsClipboard = { deviceIp:state.selectedDeviceIp, sourcePath:item.path, name:item.name, isDirectory:item.isDir, move:false };
    updateFileClipboardState();
    toast(I18n.format('已复制 {0}，请选择目标文件夹后粘贴', item.name));
  });
  q('#cutDeviceFile').addEventListener('click', () => {
    const item = state.fs.selected;
    if (!item) return;
    state.fsClipboard = { deviceIp:state.selectedDeviceIp, sourcePath:item.path, name:item.name, isDirectory:item.isDir, move:true };
    updateFileClipboardState();
    toast(I18n.format('已剪切 {0}，请选择目标文件夹后粘贴', item.name));
  });
  q('#pasteDeviceFile').addEventListener('click', () => {
    const clipboard = state.fsClipboard;
    if (!clipboard) return;
    if (clipboard.deviceIp !== state.selectedDeviceIp) return toast(I18n.t('剪贴板项目来自另一台设备'), true);
    const parent = q('#deviceFilesPath').value.trim() || '/sd';
    let name = clipboard.name;
    let destinationPath = joinDevicePath(parent, name);
    if (destinationPath.toLowerCase() === clipboard.sourcePath.toLowerCase()) {
      if (clipboard.move) return toast(I18n.t('源文件已经在当前目录中'), true);
      name = copiedName(name);
      destinationPath = joinDevicePath(parent, name);
    }
    post('device.fs.paste', { sourcePath:clipboard.sourcePath, destinationPath, isDirectory:clipboard.isDirectory, move:clipboard.move, parent });
    if (clipboard.move) { state.fsClipboard = null; updateFileClipboardState(); }
  });
  q('#editLuaFile').addEventListener('click', () => {
    const item = state.fs.selected;
    if (!item || !/\.lua$/i.test(item.name || '')) return;
    gotoPage('devtools');
    q('#luaFilePath').value = '/sd/apps/devrun/main.lua';
    q('#luaCodeEditor').value = state.fs.previewText || '';
    state.loadedLuaCode = '\u0000';
    updateLuaMeta();
    q('#luaCodeState').textContent = I18n.format('已从 {0} 导入，尚未保存', item.path);
  });
  q('#readLuaCode').addEventListener('click', () => post('device.lua.read', { path:q('#luaFilePath').value.trim() }));
  q('#saveLuaCode').addEventListener('click', () => post('device.lua.save', { path:q('#luaFilePath').value.trim(), code:q('#luaCodeEditor').value }));
  q('#runLuaCode').addEventListener('click', () => post('device.lua.run', { path:q('#luaFilePath').value.trim(), code:q('#luaCodeEditor').value }));
  q('#luaCodeEditor').addEventListener('input', updateLuaMeta);
  q('#luaCodeEditor').addEventListener('keydown', event => {
    if (event.key === 'Tab') {
      event.preventDefault();
      const editor = event.currentTarget;
      const start = editor.selectionStart, end = editor.selectionEnd;
      editor.setRangeText('  ', start, end, 'end');
      updateLuaMeta();
    } else if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's') {
      event.preventDefault();
      post('device.lua.save', { path:q('#luaFilePath').value.trim(), code:event.currentTarget.value });
    }
  });

  window.Cubic = { receive };
  window.addEventListener('DOMContentLoaded', () => {
    I18n.apply(I18n.language, false);
    q('#headerLanguageSelect').value = I18n.language;
    updateLuaMeta();
    const params = new URLSearchParams(location.search);
    const initialPage = params.get('page');
    if (initialPage && pages[initialPage]) gotoPage(initialPage);
    const initialTab = params.get('tab');
    if (initialTab) q(`[data-control-tab="${CSS.escape(initialTab)}"]`)?.click();
    post('app.bootstrap');
  });
})();
