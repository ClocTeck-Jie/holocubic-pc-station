'use strict';
const call=(name,data)=>window.desktop.call(name,data);
const api=(route,method='GET',body,address=state.prefs.active)=>call('api',{route,method,body,address});
const state={prefs:{devices:[],active:''},version:'0.2.0',system:null,services:[],settings:{},settingsLoaded:false,page:'home',drawer:null,modal:null,browser:{},online:false,busy:false,dirty:false,scan:null,scanning:false,serial:null,serialText:'',logs:'',downloads:[],pc:null};
const btn=(text,attrs='',kind='')=>`<button class="btn ${kind}" ${attrs}>${text}</button>`;
const selectOptions=(items,value)=>items.map(([v,label])=>`<option value="${esc(v)}" ${String(v)===String(value)?'selected':''}>${esc(label)}</option>`).join('');
const header=(title,subtitle='',actions='')=>`<div class="page-head"><div><h1>${esc(title)}</h1>${subtitle?`<p>${esc(subtitle)}</p>`:''}</div><div class="head-actions">${actions}</div></div>`;
const dev=()=>state.prefs.devices.find(d=>d.ip===state.prefs.active)||{name:'Cubic',ip:state.prefs.active};
const delay=ms=>new Promise(r=>setTimeout(r,ms));
function fillIcons(root=document){root.querySelectorAll('[data-icon]').forEach(el=>el.innerHTML=icon(el.dataset.icon))}
function toast(message){$('#toast').textContent=String(message);$('#toast').classList.remove('hidden');clearTimeout(toast.timer);toast.timer=setTimeout(()=>$('#toast').classList.add('hidden'),4500)}
const appearance=id=>{if(/music|mp3|Spectrum/i.test(id))return ['music','#20b96a'];if(/clock|calendar/i.test(id))return ['clock','#9455eb'];if(/weather/i.test(id))return ['cloud','#36a0f8'];if(/photo/i.test(id))return ['image','#e887a8'];if(/video|mjpeg/i.test(id))return ['video','#ed5778'];if(/retro|2048|hidpad/i.test(id))return ['game','#23b5af'];if(/monitor|mirror/i.test(id))return ['monitor','#527fdb'];if(/schedule/i.test(id))return ['alarm','#9455eb'];if(/devtools/i.test(id))return ['tool','#24b678'];return ['cube','#6284bb']};
const appVisual=(a,large=false)=>{const id=a?.id||'',url=state.appIcons?.[id];return `<span class="app-icon actual-app-icon ${large?'hero-icon':''}" data-app-icon="${esc(id)}">${url?`<img src="${url}" alt="">`:icon(appearance(id)[0])}</span>`};
async function loadAppIcons(){const address=state.prefs.active,apps=state.system?.installed_apps||state.system?.apps||[];const key=address+'|'+apps.map(a=>a.id+':'+a.version).join('|');if(state.iconKey===key&&Date.now()-(state.iconTime||0)<60000)return;state.iconKey=key;state.iconTime=Date.now();const icons=await call('appIcons',{apps,address,force:!!state.forceIcons});state.forceIcons=false;if(state.prefs.active!==address)return;state.appIcons=icons;if(Object.values(icons).some(v=>!v))state.iconTime=0;$$('[data-app-icon]').forEach(el=>{const src=icons[el.dataset.appIcon];if(src)el.innerHTML=`<img src="${src}" alt="">`})}

const running=yes=>`<span class="status"><i class="dot ${yes?'':'offline'}"></i>${yes?'运行中':'未启动'}</span>`;
function syncChrome(){const d=dev();$('#activeDeviceName').textContent=d.name;$('#activeDeviceIp').textContent=d.ip;$('#sideIp').textContent=d.ip;$('#deviceSwitch .dot').classList.toggle('offline',!state.online);$('#sideDeviceName .dot').classList.toggle('offline',!state.online);$$('.sidebar nav button').forEach(b=>b.classList.toggle('active',b.dataset.page===state.page));}
function layout(){const blocked=!!state.drawer||!!state.modal||!$('#deviceMenu').classList.contains('hidden')||!!($('#fmQueue')&&!$('#fmQueue').classList.contains('hidden'));document.body.classList.toggle('has-console-panel',blocked);const slot=$('#browserSlot');if(!slot)return call('browserLayout',{visible:false});slot.classList.toggle('has-backdrop',blocked);const r=slot.getBoundingClientRect();return call('browserLayout',{bounds:{x:r.x,y:r.y,width:r.width,height:r.height},visible:state.page!=='home'&&!blocked,preserve:blocked})}
new ResizeObserver(()=>layout().catch(()=>{})).observe($('#main'));
window.addEventListener('resize',()=>layout());
function closeDrawer(){state.drawer=null;$('#drawerLayer').classList.add('hidden');layout()}
function closeModal(){state.modal=null;$('#modalLayer').classList.add('hidden');layout()}
function modal(title,subtitle,body,footer='',wide=false){state.modal=title;$('#modal').className='modal'+(wide?' wide':'');$('#modal').innerHTML=`<div class="modal-head"><div><h2>${esc(title)}</h2><p>${esc(subtitle)}</p></div><button class="icon-button" data-action="close-modal">${icon('x')}</button></div><div class="modal-body">${body}</div>${footer?`<div class="modal-foot">${footer}</div>`:''}`;$('#modalLayer').classList.remove('hidden');layout()}
function drawer(title,subtitle,body,wide=false){$('#drawer').className='drawer'+(wide?' wide':'');$('#drawer').innerHTML=`<div class="drawer-head"><div><h2>${esc(title)}</h2><p>${esc(subtitle)}</p></div><button class="icon-button" data-action="close-drawer">${icon('x')}</button></div><div class="drawer-body">${body}</div>`;$('#drawerLayer').classList.remove('hidden');layout()}
function refresh({settings=false,quiet=false}={}){
 const address=state.prefs.active;if(state.refreshRequest?.address===address){state.refreshRequest.settings||=settings;return state.refreshRequest.promise;}
 const request={address,settings,editVersion:state.settingsEditVersion||0};state.refreshRequest=request;state.busy=true;
 const current=()=>state.refreshRequest===request&&state.prefs.active===address;
 request.promise=(async()=>{try{
  const snapshot=await call('deviceSnapshot',{address});if(!current())return;acceptDeviceSnapshot(snapshot);
  if(request.settings||!state.settingsLoaded){try{const value=await api('/api/system/fs/file?path='+encodeURIComponent('/sd/apps/settings.json'),'GET',undefined,address);if(!current())return;if(!value||typeof value!=='object'||Array.isArray(value))throw Error('设置文件格式错误');request.settingsFetched=true;state.settings=value;state.settingsAddress=address;state.settingsLoaded=true;if((state.settingsEditVersion||0)===request.editVersion)state.dirty=false;}catch(e){if(!current())return;state.settingsLoaded=false;if(!quiet)toast('读取设备设置失败：'+e.message);}}
 }catch(e){if(current()){state.online=false;if(!quiet)toast('连接失败：'+e.message);}}
 finally{const active=current();if(state.refreshRequest===request){state.refreshRequest=null;state.busy=false;}if(active){syncChrome();if(state.page==='home')renderHome(!request.settingsFetched);}}
 })();return request.promise;
}
function acceptDeviceSnapshot(value){
 if(value.address!==state.prefs.active)return;
 state.online=value.online;
 if(value.system){state.system=value.system;state.services=value.services||[];state.servicesError=value.servicesError||'';}
 if(value.metadata)state.appMetadata=value.metadata;
 const keys={'zh-CN':['name_zh_cn','name_zh'],'zh-TW':['name_zh_tw','name_zh_hant'],ja:['name_ja'],en:['name_en'],de:['name_de','name_en']}[HoloI18n.language]||['name_en'];
 const localize=a=>{const meta=state.appMetadata?.[a.id]||{};return {...a,name:keys.map(k=>meta[k]||a[k]).find(Boolean)||a.name||a.id}};
 if(state.system){state.system={...state.system,apps:(state.system.apps||[]).map(localize),installed_apps:(state.system.installed_apps||[]).map(localize),current_app:state.system.current_app?localize(state.system.current_app):null};}
 syncChrome();if(state.page==='home')renderHome(true);if(value.online)loadAppIcons().catch(()=>{state.iconTime=0;});
}
function home(){closeDrawer();closeModal();$('#deviceMenu').classList.add('hidden');state.page='home';state.browser={};$('#main').classList.remove('browser-mode','files-mode');layout();renderHome();refresh({quiet:true})}
function renderHome(preserve=false){
 const main=$('#main'),scroll=main.scrollTop;
 const draft=preserve||state.dirty?$('.settings-card'):null;
 const appListScroll=$('.dashboard-app-list')?.scrollTop||0;
 syncChrome();const s=state.system,settings=state.settings,current=s?.current_app,apps=s?.apps||[],installed=s?.installed_apps||[];
 if(!state.online&&s&&$('.hero')){document.body.classList.add('device-stale');const status=$('.network-copy .status');if(status)status.textContent='设备暂时离线，显示上次状态';return;}document.body.classList.remove('device-stale');
 if(!s||!state.online){$('#main').innerHTML=header('设备控制面板',dev().ip)+`<section class="card empty-state">${icon('wifi')}<strong>${state.busy?'正在连接设备':'暂时无法连接设备'}</strong><p>${state.busy?'正在读取真实设备状态…':'请确认设备已开机，并与电脑处于同一局域网。'}</p>${btn('重新连接','data-action="refresh"','primary')} ${btn('扫描局域网设备','data-action="scan"')}</section>`;return}
 const services=installed.filter(a=>a.kind==='service').map(a=>({...a,...state.services.find(s=>s.id===a.id)}));
 const markup=header('设备控制面板',dev().name,btn('完整设备页面','data-route="/main"')+btn(icon('back')+'返回启动器','data-action="exit-app"'))+`<section class="card hero">${appVisual(current,true)}<div class="hero-copy"><span class="eyebrow">当前应用</span><h2>${esc(current?.name||'启动器')}</h2>${running(!!current)}</div><div class="hero-actions">${btn('进入应用页面','data-action="current-app"'+(!s.current_webui?' disabled':''),'primary')}${btn('退出应用','data-action="exit-app"'+(!current?' disabled':''))}</div><div class="hero-stats"><div><strong>${apps.length}</strong><span>个应用</span></div><div><strong>${services.filter(s=>s.running).length}</strong><span>个服务</span></div></div></section><div class="dashboard-grid"><section class="card"><div class="card-head"><h2>设备应用</h2></div><button class="store-entry" data-page="store">${appIcon('store','#3878ff')}<div><b>应用商店</b><small>安装、更新与管理设备应用</small></div>${icon('arrow')}</button><div class="dashboard-app-list" role="region" aria-label="设备应用" tabindex="0">${apps.map(a=>appRow(a)).join('')}</div></section><div class="right-stack"><section class="card"><div class="card-head"><h2>设备服务</h2><button class="text-button" data-route="/apps/launcher/store-preview.html?kind=service">管理 ›</button></div>${state.servicesError?'<p class="small-note">服务状态读取失败，显示上次结果</p>':''}${services.map(a=>`<div class="service-row">${appVisual(a)}<b>${esc(a.name)}</b>${running(a.running)}<div class="service-controls">${btn(a.running?'停止':'启动',`data-service-control="${esc(a.id)}" data-operation="${a.running?'stop':'start'}"`,'small')}${btn('设置',`data-service="${esc(a.id)}"`,'small')}</div></div>`).join('')||'<p class="small-note">设备没有已安装服务</p>'}</section><section class="card"><div class="card-head"><h2>连接状态</h2><button class="text-button" data-page="network">管理 ›</button></div><div class="network-summary">${icon('wifi')}<div class="network-copy"><strong>${esc(s.wifi?.sta_ssid||'设备热点')}</strong><span class="status"><i class="dot"></i>${esc(s.wifi?.mode||'已连接')} · ${s.wifi?.sta_rssi?esc(s.wifi.sta_rssi)+' dBm':'在线'}</span></div><div class="ip-group"><small>IP 地址</small><strong>${esc(s.wifi?.sta_ip||s.wifi?.ap_ip||dev().ip)}</strong></div></div></section></div></div><section class="card settings-card"><div class="card-head"><h2>常用设置</h2><small id="dirtyNote">${state.settingsLoaded?'设置直接保存到当前设备':'设备设置读取失败，请刷新后再保存'}</small></div><form id="deviceSettingsForm"><fieldset ${!state.settingsLoaded?'disabled':''} style="border:0;padding:0;margin:0"><div class="settings-grid"><div class="inline-field"><label for="timezone">时区</label><select id="timezone">${selectOptions(timezones.map(z=>[z.value,z.label]),settings.timezone||'CST-8')}</select></div><div class="inline-field"><label for="weatherAddress">天气地址</label><input id="weatherAddress" value="${esc(settings.weather_address||settings.weatherAddress||'')}"></div><div class="inline-field"><label for="startup">开机自启动</label><select id="startup">${selectOptions([['','关闭自启动'],...apps.map(a=>[a.id,a.name])],settings.autostart_enabled?settings.autostart_app_id:'')}</select></div><div class="inline-field"><label for="brightness">屏幕亮度</label><input type="range" min="1" max="100" id="brightness" value="${Number(settings.brightness||80)}"><span id="brightnessText" class="range-value">${Number(settings.brightness||80)}%</span></div><div class="inline-field"><label for="apMode">无线热点</label><select id="apMode">${selectOptions([['true','保持开启'],['false','有网络时关闭']],settings.ap_enabled!==false?'true':'false')}</select></div><div class="inline-field"><label for="language">设备语言</label><select id="language" data-no-i18n>${selectOptions([['zh-CN','简体中文'],['zh-TW','繁體中文'],['en','English'],['ja','日本語']],settings.language||'zh-CN')}</select></div></div><div class="settings-footer"><button type="submit" class="btn primary">保存设置</button>${btn('恢复默认','type="button" data-action="reset-settings"','ghost')}${btn('指示灯设置','type="button" data-action="led"','ghost')}${btn('唤醒屏幕','type="button" data-action="wake"','ghost')}</div></fieldset></form></section><div class="footer-line"><span>Launcher ${esc(installed.find(a=>a.kind==='launcher')?.version||'--')} · 固件 ${esc(s.firmware_update?.current_version||'--')}</span><button class="text-button" data-action="firmware">设备固件更新 ›</button></div>`;
 const template=document.createElement('template');template.innerHTML=markup;
 if(preserve&&main.querySelector('.hero')){
  const selectors=['.page-head','.hero','.dashboard-app-list','.right-stack > .card:first-child','.right-stack > .card:last-child','.footer-line'];
  for(const selector of selectors){const old=main.querySelector(selector),next=template.content.querySelector(selector);if(old&&next&&old.dataset.signature!==next.innerHTML){next.dataset.signature=next.innerHTML;old.replaceWith(next);}}
 }else{main.replaceChildren(template.content);if(draft&&$('.settings-card'))$('.settings-card').replaceWith(draft);}
 main.scrollTop=scroll;
 const startup=$('#startup');if(preserve&&startup&&document.activeElement!==startup){const selected=startup.value;const options=selectOptions([['','关闭自启动'],...apps.map(a=>[a.id,a.name])],selected);if(startup.dataset.options!==options){startup.innerHTML=options;if(selected&&!apps.some(a=>a.id===selected)){const option=new Option(selected,selected,true,true);startup.add(option);}startup.dataset.options=options;}}
 const appList=$('.dashboard-app-list');if(appList)appList.scrollTop=appListScroll;
}
function appRow(a){return `<div class="app-row">${appVisual(a)}<span class="row-title">${esc(a.name)}</span>${state.system?.current_app?.id===a.id?running(true):''}${btn(state.system?.current_app?.id===a.id?'应用页面':'打开',`data-app="${esc(a.id)}"`,'small')}</div>`}
async function openBrowser(route,title='设备页面',page='web',method='openWeb'){
 const address=state.prefs.active;
 closeDrawer();closeModal();$('#deviceMenu').classList.add('hidden');state.page=page;state.browser={};$('#main').classList.remove('files-mode');$('#main').classList.add('browser-mode');$('#main').innerHTML=`<div class="browser-bar"><strong>${esc(title)}</strong><i class="loading-dot" id="webLoading"></i><span class="browser-address" id="browserAddress">正在打开…</span><button class="text-button" data-action="refresh">刷新</button><button class="text-button" data-page="home">返回控制面板</button></div><div class="browser-slot" id="browserSlot"><img id="browserBackdrop" class="browser-backdrop hidden" alt=""><div class="browser-status" id="browserStatus">正在连接设备页面…</div></div>`;syncChrome();const slot=$('#browserSlot');const current=()=>address===state.prefs.active&&slot===$('#browserSlot');await layout();if(!current())return;try{await call(method,{route,address});if(current())await layout()}catch(e){if(!current())return;$('#browserBackdrop')?.classList.add('hidden');await call('browserLayout',{visible:false});$('#browserStatus').textContent='无法打开页面：'+e.message+'。可点击刷新重试。';toast(e.message)}
}
async function launchApp(id){
 if(state.launchRequest)throw Error('应用正在启动，请稍候');const address=state.prefs.active,request={address,id};state.launchRequest=request;const current=()=>state.prefs.active===address;
 try{
  if(state.system?.current_app?.id!==id){await call('prepareApp',{id,address});if(!current())return;await safeExitPrepare(address);if(!current())return;await api('/api/system/launch?id='+encodeURIComponent(id),'POST',undefined,address);if(!current())return;toast('正在启动应用…');}
  for(let n=0;n<18;n++){if(!current())return;const value=await api('/api/system/state','GET',undefined,address);if(!current())return;state.system=value;
   if(value.current_app?.id===id){state.online=true;if(value.current_webui&&value.current_route_base){closeModal();closeDrawer();await call('configureCompanion',{address}).catch(e=>{if(current())toast('服务自动配置：'+e.message);});if(!current())return;await call('openAppWindow',{route:value.current_route_base,title:value.current_app.name,id,address});if(current()&&state.page==='home'&&!state.dirty){const scroll=$('#main').scrollTop;renderHome();$('#main').scrollTop=scroll;}return;}if(n>6){closeModal();home();toast('应用已启动，此应用没有控制页面');return;}}
   await delay(350);
  }throw Error('应用启动时间较长，请稍后刷新设备状态');
 }finally{if(state.launchRequest===request)state.launchRequest=null;}
}
async function safeExitPrepare(address=state.prefs.active){if(state.system?.current_app?.id==='mjpeg_player'){await api('/api/mjpeg-player/prepare-exit','POST',undefined,address);await delay(100);}}
async function openService(id){const address=state.prefs.active;let runtime=state.services.find(s=>s.id===id);if(!runtime?.running){await api('/api/system/services/start?id='+encodeURIComponent(id),'POST',undefined,address);for(let n=0;n<10;n++){if(address!==state.prefs.active)return;await delay(250);const value=await api('/api/system/services','GET',undefined,address);if(address!==state.prefs.active)return;state.services=value.services||[];runtime=state.services.find(s=>s.id===id);if(runtime?.running&&runtime.route_base)break;}}
 if(address!==state.prefs.active)return;if(!runtime?.route_base)throw Error('服务尚未提供控制页面，请稍后重试');await openBrowser(runtime.route_base,runtime.name,'service');
}
function deviceMenu(){const menu=$('#deviceMenu');menu.innerHTML=`<div class="popover-title">我的设备</div>${state.prefs.devices.map(d=>`<button class="device-option ${d.ip===state.prefs.active?'active':''}" data-device="${esc(d.ip)}"><i class="dot ${(d.ip===state.prefs.active?state.online:d.online)?'':'offline'}"></i><div><b>${esc(d.name)}</b><small>${esc(d.ip)}</small></div>${d.ip===state.prefs.active?icon('check'):''}</button>`).join('')}<div class="popover-footer"><button data-action="scan">${icon('search')}扫描局域网设备</button><button data-action="add-device">${icon('plus')}添加设备</button></div>`;menu.classList.toggle('hidden');$('#deviceSwitch').setAttribute('aria-expanded',String(!menu.classList.contains('hidden')));layout()}
function addDevice(){closeDrawer();$('#deviceMenu').classList.add('hidden');modal('添加设备','输入设备地址，连接后保存到列表。',`<form id="addDeviceForm"><label class="field"><span>设备名称</span><input id="newDeviceName" placeholder="例如：桌面 Cubic" maxlength="50"></label><label class="field"><span>IP 地址或 .local 地址</span><input id="newDeviceIp" placeholder="192.168.0.218" required></label><label class="checkbox-row"><input type="checkbox" id="saveOffline">暂时离线，先保存地址</label><div class="modal-foot">${btn('取消','type="button" data-action="close-modal"')}<button class="btn primary" type="submit">添加设备</button></div></form>`);$('#newDeviceIp').focus()}
function renderScan(){const s=state.scan||{found:[],done:0,total:0,ranges:[]};modal('扫描局域网设备','自动识别当前网卡所在子网中的 Cubic 设备。',`<div class="scan-progress"><span>${state.scanning?'正在扫描':'扫描结束'} · ${s.done} / ${s.total} · ${esc((s.ranges||[]).join('、'))}</span><div class="progress-track"><div class="progress-fill" style="width:${s.total?s.done/s.total*100:0}%"></div></div></div><div>${s.found.length?s.found.map(d=>`<div class="scan-result">${appIcon('cube','#5682de')}<div><b>${esc(d.ip)}</b><small>${esc(d.system?.current_app?.name||'Launcher')} · 固件 ${esc(d.system?.firmware_update?.current_version||'--')} · 已自动加入列表</small></div>${btn(state.prefs.devices.some(x=>x.ip===d.ip)?'连接':'连接',`data-found="${esc(d.ip)}"`,'small primary')}</div>`).join(''):`<div class="no-devices">${state.scanning?'正在寻找设备…':'没有发现设备，可以手动输入地址。'}</div>`}</div>`,btn('手动添加','data-action="add-device"')+btn(state.scanning?'停止扫描':'重新扫描',`data-action="${state.scanning?'cancel-scan':'restart-scan'}"`,'primary'),true)}
async function scan(){if(state.scanning){renderScan();return}state.scanning=true;state.scan={found:[],done:0,total:0,ranges:[]};renderScan();$('#scanLabel').textContent='正在扫描…';try{state.scan=await call('scan');if(state.scan.prefs)state.prefs=state.scan.prefs}finally{state.scanning=false;$('#scanLabel').textContent='扫描局域网设备';if(state.modal==='扫描局域网设备')renderScan()}}
async function openDrawer(kind){closeModal();$('#deviceMenu').classList.add('hidden');state.drawer=kind;
 if(kind==='advanced'){drawer('高级工具','串口、日志与设备开发入口。',`<div class="tool-grid">${[['serial-wifi','wifi','串口配网','扫描热点、连接 Wi-Fi'],['serial','link','串口终端','连接与收发命令'],['logs','file','运行日志','查看与导出软件日志'],['downloads','download','下载记录','本次运行中的下载任务'],['devtools','code','设备开发工具','SD 卡文件与 Lua 编辑']].map(([id,i,t,s])=>`<button class="tool-entry" data-tool="${id}">${icon(i)}<div><b>${t}</b><small>${s}</small></div></button>`).join('')}</div><p class="small-note" style="margin-top:24px">应用安装和媒体转换进度显示在对应设备页面中。</p>`);return}
 if(kind==='settings'){drawer('软件设置','设备列表与控制台更新。',`<section class="settings-section"><div class="about-brand">${icon('cube')}<div><b>holocubic控制台</b><small>v${esc(state.version)} · 设备 Web 控制</small></div></div><h3>软件更新</h3><label class="field"><span>更新源地址</span><input id="updateFeed" placeholder="https://…/latest.json" value="${esc(state.prefs.updateFeed||'')}"><small>也可以直接导入发布者提供的 ZIP 更新包。</small></label><div class="pc-service-actions">${btn('保存地址','data-action="save-feed"','small')}${btn('检查更新','data-action="check-update"','small primary')}</div><div id="updateMessage" class="update-message"></div><div id="updateActions"></div>${btn('导入本地更新包','data-action="import-update"')}</section><section class="settings-section"><h3>设备管理</h3>${state.prefs.devices.map(d=>`<div class="scan-result"><div><b>${esc(d.name)}</b><small>${esc(d.ip)}</small></div>${btn('重命名',`data-rename-device="${esc(d.ip)}"`,'small')}</div>`).join('')}<p class="small-note">设备信息保存在本机，可修改设备的显示名称。</p></section>`);return}
 if(kind==='services'){drawer('电脑服务','管理本机为设备提供的服务。','<p class="small-note">正在读取服务状态…</p>');state.pc=await call('pcStatus');if(state.drawer!=='services')return;renderPc();return}
}
const toolBack=()=>`<button class="tool-back" data-drawer="advanced">${icon('back')}高级工具</button>`;
async function tool(id){if(id==='serial-wifi')return showSerialWifi();if(id==='devtools')return openService('devtools');state.drawer=id;if(id==='serial'){drawer('串口终端','连接 USB 串口，查看设备输出并发送命令。',toolBack()+'<p class="small-note">正在读取串口…</p>',true);const result=await call('serialStatus');state.serial=result.connection;state.serialMonitor=result.monitor;state.serialText=result.monitor?.text||'';if(state.drawer==='serial')renderSerial(result.ports)}else if(id==='logs'){state.logs=await call('logs');if(state.drawer==='logs')drawer('运行日志','真实连接、操作结果与软件错误。',toolBack()+`<div class="log-toolbar"><span class="small-note">最近运行记录</span>${btn('串口日志文件夹','data-action="serial-log-folder"','small')}${btn('导出日志','data-action="export-logs"','small')}</div><pre id="logText" class="terminal">${esc(state.logs)}</pre>`,true)}else if(id==='downloads'){state.downloads=await call('downloads');renderDownloads()}}
function renderDownloads(){if(state.drawer!=='downloads')return;drawer('下载记录','设备文件的真实下载进度。',toolBack()+state.downloads.map(t=>`<div class="task-row"><strong>${esc(t.name)}</strong><small>${esc(t.state)} · ${(t.received/1048576).toFixed(1)} MB / ${(t.total/1048576).toFixed(1)} MB</small><div class="progress-track"><div class="progress-fill" style="width:${t.total?t.received/t.total*100:0}%"></div></div>${t.state==='已完成'?btn('打开所在文件夹',`data-download="${esc(t.id)}"`,'small'):''}</div>`).join('')+(state.downloads.length?'':'<p class="small-note">本次运行还没有下载记录。</p>'))}
function updateReady(result){if(!result)return;$('#updateMessage').textContent='v'+result.version+' 更新包已校验，可以安装。';$('#updateActions').innerHTML=btn('安装并重启','data-action="apply-update"','primary')}
async function saveSettings(){
 const address=state.prefs.active,editVersion=state.settingsEditVersion||0;if(!state.settingsLoaded||state.settingsAddress&&state.settingsAddress!==address)throw Error('请先重新读取设备设置');
 const next={...state.settings,timezone:$('#timezone').value,weather_address:$('#weatherAddress').value.trim(),language:$('#language').value,ap_enabled:$('#apMode').value==='true',brightness:Number($('#brightness').value),autostart_enabled:!!$('#startup').value,autostart_app_id:$('#startup').value};
 await api('/api/system/fs/upload?path='+encodeURIComponent('/sd/apps/settings.json'),'PUT',JSON.stringify(next,null,2),address);
 const saved=await api('/api/system/fs/file?path='+encodeURIComponent('/sd/apps/settings.json'),'GET',undefined,address);
 if(saved?.language!==next.language)throw Error('设备语言保存校验失败，请重试');
 try{await api('/display/api/settings?brightness='+encodeURIComponent(next.brightness),'POST',undefined,address);const applied=await call('deviceSettingsApplied',{address,language:next.language});if(address===state.prefs.active)toast(applied.launcherRefreshed?'设置已保存到设备':'设备语言已保存，重新打开设备应用后生效');}catch(e){if(address===state.prefs.active)toast('设置已保存，但应用设置失败：'+e.message);}
 if(address!==state.prefs.active)return;state.settings=next;if((state.settingsEditVersion||0)===editVersion){state.dirty=false;if(state.page==='home')renderHome();}
}
const actions={
 'close-modal':closeModal,'close-drawer':closeDrawer,'add-device':addDevice,'scan':scan,'restart-scan':scan,'cancel-scan':()=>call('cancelScan'),
 refresh:async()=>{if(state.page==='files')return window.filesUI.refresh();if(state.page!=='home')return call('browserAction',{action:'reload'});state.dirty=false;await refresh({settings:true})},
 'all-apps':()=>{closeDrawer();modal('设备应用','选择应用后会自动打开它的实际控制页面。',`<div class="app-list-grid all-apps">${(state.system?.apps||[]).map(appRow).join('')}</div>`,'',true)},
 'current-app':async()=>{const address=state.prefs.active;await refresh({quiet:true});if(address!==state.prefs.active)return;if(state.system?.current_webui&&state.system.current_route_base)await call('openAppWindow',{route:state.system.current_route_base,title:state.system.current_app?.name||'应用页面',id:state.system.current_app?.id});else toast('当前应用没有 Web 控制页面')},
 'exit-app':async()=>{const address=state.prefs.active;await safeExitPrepare(address);if(address!==state.prefs.active)return;await api('/api/system/exit','POST',undefined,address);await delay(300);if(address!==state.prefs.active)return;await refresh();toast('已返回启动器')},
 'wake':async()=>{await api('/display/api/wake','POST');toast('已唤醒屏幕')},
 'reset-settings':()=>{if(!state.settingsLoaded)return;state.settingsEditVersion=(state.settingsEditVersion||0)+1;$('#timezone').value='CST-8';$('#weatherAddress').value='new york';$('#apMode').value='true';$('#brightness').value='80';$('#brightnessText').textContent='80%';$('#startup').value='';state.dirty=true;$('#dirtyNote').textContent='默认值已填入，点击保存设置后生效'},
 'firmware':()=>openBrowser('/main','设备固件与系统设置','firmware'),
 'led':async()=>{const led=await api('/display/api/led');const color=led.color||led.led||'#ffffff';modal('指示灯设置','调整灯光颜色与亮度，点击应用后发送到设备。',`<label class="field"><span>颜色</span><input type="color" id="ledColor" value="${/^#[\da-f]{6}$/i.test(color)?color:'#ffffff'}"></label><label class="field"><span>亮度</span><input type="range" id="ledBrightness" min="0" max="100" value="100"></label>`,btn('取消','data-action="close-modal"')+btn('应用','data-action="apply-led"','primary'))},
 'apply-led':async()=>{const c=$('#ledColor').value,v=Number($('#ledBrightness').value)/100;const scaled='#'+[1,3,5].map(p=>Math.round(parseInt(c.slice(p,p+2),16)*v).toString(16).padStart(2,'0')).join('');await api('/display/api/led?color='+encodeURIComponent(scaled),'POST');closeModal();toast('指示灯已更新')},
 'serial-log-folder':()=>call('serialLogFolder'),
 'serial-open':async()=>{state.serial=await call('serialOpen',{port:$('#serialPort').value,baud:Number($('#serialBaud').value)});await tool('serial')},
 'serial-close':async()=>{await call('serialClose');state.serial=null;await tool('serial')},
 'serial-clear':async()=>{await call('serialClear');state.serialText='';if($('#serialText'))$('#serialText').textContent=''},
 'export-logs':async()=>{if(await call('exportLogs'))toast('日志已导出')},
 
 
 
 'holopet-ai-install':async()=>{await call('holopetAiInstall');toast('已配置，请重启客户端并审查 Hook');await openPcSettings('holopet')},
 'holopet-ai-test':async()=>{await call('holopetAiTest');toast('测试状态已发送');await refreshHolopetAi()},
 'holopet-ai-refresh':()=>refreshHolopetAi(),
 'holopet-ai-extension':()=>call('holopetAiExtension'),
 'holopet-ai-help':()=>call('holopetAiHelp'),
 'holopet-account-connect':()=>holopetAccountAction('holopetAccountConnect',{type:'chatgpt'}),
 'holopet-account-device':()=>holopetAccountAction('holopetAccountConnect',{type:'chatgptDeviceCode'}),
 'holopet-account-refresh':()=>holopetAccountAction('holopetAccountRefresh'),
 'holopet-account-disconnect':()=>holopetAccountAction('holopetAccountDisconnect'),
 'holopet-account-cancel':()=>holopetAccountAction('holopetAccountCancel'),
 'holopet-account-reopen':()=>holopetAccountAction('holopetAccountReopen'),
 'monitor-elevate':async()=>{const ok=await call('monitorElevate');toast(ok?'管理员传感器已启动':'未启动管理员传感器');await refreshMonitorSnapshot()},
 'monitor-refresh':()=>refreshMonitorSnapshot(),
 'monitor-save':()=>saveMonitorBindings(),
 'monitor-reset':()=>resetMonitorBindings(),
 'serial-wifi-scan':()=>call('serialWifiScan',{port:$('#wifiSerialPort').value,baud:Number($('#wifiSerialBaud').value)}),
 'serial-wifi-cancel':()=>call('serialWifiCancel'),
 'launch-wifi-guide':async()=>{if(!state.online)throw Error('设备未联网，请在设备上手动打开 WiFi Setting Guide');await launchApp('wifi_guide')},
 'save-feed':async()=>{const url=$('#updateFeed').value.trim();await call('saveFeed',{url});state.prefs.updateFeed=url;toast('更新源已保存')},
 'check-update':async()=>{await actions['save-feed']();$('#updateMessage').textContent='正在检查更新…';const r=await call('checkUpdate');$('#updateMessage').textContent=r.available?'发现 v'+r.version+'\n'+r.notes:'当前已是最新版本';$('#updateActions').innerHTML=r.available?btn('下载更新','data-action="download-update"','primary'):''},
 'import-update':async()=>updateReady(await call('importUpdate')),
 'download-update':async()=>updateReady(await call('downloadUpdate')),
 'apply-update':()=>call('applyUpdate')
};
document.addEventListener('click',async event=>{
 const target=event.target.closest('button');if(!target)return;
 try{
  if(target.dataset.window)return window.desktop.windowAction(target.dataset.window);
  if(target.id==='deviceSwitch')return deviceMenu();
  if(target.id==='backBtn'||target.id==='forwardBtn')return await call('browserAction',{action:target.id==='backBtn'?'back':'forward'});
  target.disabled=true;
  if(target.dataset.pcDeviceApp){await enterServiceApp(target.dataset.pcDeviceApp)}
  else if(target.dataset.pcStart){await call('serviceStart',{id:target.dataset.pcStart});await refreshPc()}
  else if(target.dataset.pcStop){await call('serviceStop',{id:target.dataset.pcStop});await refreshPc()}
  else if(target.dataset.pcPage)await call('pcServicePage',{id:target.dataset.pcPage});
  else if(target.dataset.pcConfig)await openPcSettings(target.dataset.pcConfig);
  else if(target.dataset.pcSync){const id=target.dataset.pcSync,s=state.pc.services.find(x=>x.id===id);if(!s.apps.includes(state.system?.current_app?.id?.toLowerCase()))throw Error('请先打开对应的设备应用：'+s.apps[0]);await call('configureCompanion');toast('电脑服务已重新配置')}
  else if(target.dataset.serialSsid){$('#wifiSerialSsid').value=target.dataset.serialSsid;$('#wifiSerialPassword').focus()}
  else if(target.dataset.page){const p=target.dataset.page;if(p==='home')home();else await openBrowser({media:'/main#media',network:'/main#wifi',store:'/apps/launcher/store-preview.html'}[p],{media:'媒体资源',network:'网络配置',store:'应用商店'}[p],p)}
  else if(target.dataset.route)await openBrowser(target.dataset.route,target.textContent.trim());
  else if(target.dataset.app)await launchApp(target.dataset.app);
  else if(target.dataset.serviceControl){await api('/api/system/services/'+target.dataset.operation+'?id='+encodeURIComponent(target.dataset.serviceControl),'POST');await delay(500);await refresh();toast('服务操作已提交')}
  else if(target.dataset.service)await openService(target.dataset.service);
  else if(target.dataset.drawer)await openDrawer(target.dataset.drawer);
  else if(target.dataset.tool)await tool(target.dataset.tool);
  else if(target.dataset.action)await actions[target.dataset.action]?.();
  else if(target.dataset.device){state.prefs=await call('selectDevice',{ip:target.dataset.device});state.system=null;state.appMetadata={};state.services=[];state.appIcons={};state.iconKey='';state.settingsLoaded=false;state.dirty=false;home()}
  else if(target.dataset.found){state.prefs=await call('saveDevice',{ip:target.dataset.found,verify:true});await call('selectDevice',{ip:state.prefs.active});state.system=null;state.appMetadata={};state.services=[];state.appIcons={};state.iconKey='';state.settingsLoaded=false;state.dirty=false;home()}
  else if(target.dataset.renameDevice){const d=state.prefs.devices.find(d=>d.ip===target.dataset.renameDevice);state.renameDevice=d.ip;modal('设备重命名','名称只保存在此电脑，用于识别设备。',`<form id="renameDeviceForm"><label class="field"><span>设备名称</span><input id="renameDeviceName" data-no-i18n maxlength="50" value="${esc(d.name)}" required></label><div class="modal-foot"><button type="button" class="btn" data-action="close-modal">取消</button><button type="submit" class="btn primary">保存</button></div></form>`);setTimeout(()=>{$('#renameDeviceName')?.focus();$('#renameDeviceName')?.select();},0);}
  else if(target.dataset.removeDevice){state.prefs=await call('removeDevice',{ip:target.dataset.removeDevice});await call('selectDevice',{ip:state.prefs.active});state.system=null;state.appMetadata={};state.services=[];state.appIcons={};state.iconKey='';state.settingsLoaded=false;state.dirty=false;home();await openDrawer('settings')}
  else if(target.dataset.download)await call('revealDownload',{id:target.dataset.download});
 }catch(e){toast(e.message);if($('#updateMessage')&&target.dataset.action?.includes('update'))$('#updateMessage').textContent=e.message}finally{if(target.isConnected)target.disabled=false}
});
document.addEventListener('submit',async event=>{event.preventDefault();const button=event.target.querySelector('button[type=submit],button.btn.primary');if(button)button.disabled=true;try{
 if(event.target.id==='deviceSettingsForm')await saveSettings();
 if(event.target.id==='renameDeviceForm'){state.prefs=await call('renameDevice',{ip:state.renameDevice,name:$('#renameDeviceName').value});closeModal();syncChrome();if(state.page==='home')renderHome();else if(state.page==='files')window.filesUI.relabel();await openDrawer('settings');toast('设备名称已保存');}
 if(event.target.id==='addDeviceForm'){state.prefs=await call('saveDevice',{ip:$('#newDeviceIp').value,name:$('#newDeviceName').value,verify:!$('#saveOffline').checked});await call('selectDevice',{ip:state.prefs.active});state.system=null;state.appMetadata={};state.services=[];state.appIcons={};state.iconKey='';state.settingsLoaded=false;state.dirty=false;home();toast('设备已添加')}
 if(event.target.id==='mirrorForm'){await call('mirrorConfigure',{monitor:Number($('#mirrorMonitor').value),fit:$('#mirrorFit').value,region:$('#mirrorRegion').value.trim(),fps:Number($('#mirrorFps').value),quality:Number($('#mirrorQuality').value)});toast('投屏设置已保存')}
 if(event.target.id==='serialWifiForm'){const pwd=$('#wifiSerialPassword').value;await call('serialWifiProvision',{port:$('#wifiSerialPort').value,baud:Number($('#wifiSerialBaud').value),ssid:$('#wifiSerialSsid').value,pwd});$('#wifiSerialPassword').value=''}
 if(event.target.id==='serialForm'){const value=$('#serialInput').value,ending=$('#lineEnding').value;await call('serialWrite',{text:value+(ending==='lf'?'\n':ending==='crlf'?'\r\n':'')});$('#serialInput').value=''}
 }catch(e){toast(e.message)}finally{if(button?.isConnected)button.disabled=false}});
document.addEventListener('change',async e=>{try{if(e.target.id==='holopetLocalSync'){const data=await call('holopetAiLocalConfigure',{enabled:e.target.checked});renderHolopetAiStatus(data);}if(e.target.id==='autoCompanion'){await call('autoCompanion',{enabled:e.target.checked});state.prefs.autoCompanion=e.target.checked;}if(e.target.dataset.pcAutostart)await call('serviceAutoStart',{id:e.target.dataset.pcAutostart,enabled:e.target.checked});if(e.target.id==='showWifiSerialPassword')$('#wifiSerialPassword').type=e.target.checked?'text':'password'}catch(error){toast(error.message)}});
document.addEventListener('input',e=>{if(e.target.closest('#deviceSettingsForm')&&!e.target.matches('[data-ui-language]')){state.settingsEditVersion=(state.settingsEditVersion||0)+1;state.dirty=true;$('#dirtyNote').textContent='设置已修改，等待保存';if(e.target.id==='brightness')$('#brightnessText').textContent=e.target.value+'%'}});
document.addEventListener('keydown',e=>{if(e.key==='Escape'){closeModal();closeDrawer();$('#deviceMenu').classList.add('hidden');layout()}});
document.addEventListener('click',e=>{if(!e.target.closest('.device-picker')&&!$('#deviceMenu').classList.contains('hidden')){$('#deviceMenu').classList.add('hidden');layout()}});
window.desktop.onEvent(({type,value})=>{
 if(type==='ui-language'){if(state.system)acceptDeviceSnapshot({address:state.prefs.active,system:state.system,online:state.online,services:state.services});return;}
 if(type==='device-state'){acceptDeviceSnapshot(value);return;}
 if(type==='device-invalidated'&&value.address===state.prefs.active){state.iconKey='';state.iconTime=0;state.forceIcons=true;return;}
 if(type==='device-refresh-needed'){refresh({quiet:true});return;}
 if(type==='devices'){state.prefs=value;syncChrome()}
 if(type==='services-changed'&&state.drawer==='services')refreshPc().catch(e=>toast(e.message));
 if(type==='companion'&&value.state!=='working')toast(value.message);
 if(type==='serial-wifi')renderSerialNetworks(value);
 if(type==='serial-wifi-device')toast('设备已联网并加入列表：'+value.ip);
 if(type==='scan'){state.scan=value;if(state.modal==='扫描局域网设备')renderScan()}
 if(type==='browser-backdrop'&&$('#browserBackdrop')){$('#browserBackdrop').src=value.image;$('#browserBackdrop').classList.remove('hidden');}
 if(type==='browser'){state.browser=value;syncChrome();if($('#browserAddress'))$('#browserAddress').textContent=value.url;if($('#webLoading'))$('#webLoading').style.visibility=value.loading?'visible':'hidden';if(!value.loading&&$('#browserStatus'))$('#browserStatus').textContent=''}
 if(type==='browser-error'){$('#browserBackdrop')?.classList.add('hidden');toast('设备页面：'+value);call('browserLayout',{visible:false});if($('#browserStatus'))$('#browserStatus').textContent='页面加载失败：'+value+'。请点击刷新重试。'}
 if(type==='serial'){state.serialText=(state.serialText+value).slice(-100000);const el=$('#serialText');if(el){const bottom=el.scrollHeight-el.scrollTop-el.clientHeight<45;el.textContent=state.serialText;if(bottom)el.scrollTop=el.scrollHeight}}
 if(type==='serial-error'){state.serial=null;if($('#serialStatus'))$('#serialStatus').textContent='USB 串口中断，等待自动重连'}
 if(type==='serial-state')state.serial=value;
 if(type==='log'){state.logs=(state.logs+value+'\n').slice(-150000);if($('#logText')){$('#logText').textContent=state.logs;$('#logText').scrollTop=$('#logText').scrollHeight}}
 if(type==='downloads'){state.downloads=value;renderDownloads()}
 if(type==='update-download'&&$('#updateMessage'))$('#updateMessage').textContent='正在下载更新包 '+(value.received/1048576).toFixed(1)+' MB'+(value.total?' / '+(value.total/1048576).toFixed(1)+' MB':'');
});
fillIcons();
call('init').then(async result=>{state.prefs=result.prefs;HoloI18n.setLanguage(result.prefs.uiLanguage||'zh-CN');state.version=result.version;$('#versionBadge').textContent='v'+state.version;syncChrome();renderHome();await refresh({settings:true});window.addEventListener('focus',()=>refresh({quiet:true}));document.addEventListener('visibilitychange',()=>{if(document.visibilityState==='visible')refresh({quiet:true})})}).catch(e=>{$('#main').innerHTML=header('初始化失败')+`<p>${esc(e.message)}</p>`});

