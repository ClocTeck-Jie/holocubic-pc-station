'use strict';
const {app,BrowserWindow,WebContentsView,ipcMain,session,dialog:systemDialog,shell}=require('electron');
const path=require('node:path'),fs=require('node:fs'),os=require('node:os'),crypto=require('node:crypto');
const {spawn}=require('node:child_process');
const readline=require('node:readline');
const device=require('./device.cjs');
const {Companions,definitions,forApp}=require('./companions.cjs');
const {SerialWifi}=require('./serial-wifi.cjs');
const {HolopetAI}=require('./holopet-ai.cjs');
const {LocalAiMonitor}=require('./ai-local-monitor.cjs');
const {AiAccount}=require('./ai-account.cjs');
const {SerialMonitor}=require('./serial-monitor.cjs');
const {createFiles}=require('./files-main.cjs');
const {createBrowserBackdrop}=require('./browser-backdrop.cjs');
const {codes:uiLanguages,translate,localizedDialog}=require('./locales.js');
app.setName('holocubic控制台');
app.setPath('userData',path.join(app.getPath('appData'),'holocubic控制台'));
const dataDir=app.getPath('userData');fs.mkdirSync(dataDir,{recursive:true});
const logFile=path.join(dataDir,'console.log');
if(fs.existsSync(logFile)&&fs.statSync(logFile).size>5e6)fs.renameSync(logFile,logFile+'.previous');
let win,view,remoteVisible=false,viewBounds={x:215,y:140,width:1100,height:750},scanController,native,serial=null,smtc=null,promptWindow,promptData,promptEvent;
let nativeSeq=0;const nativeWait=new Map(),downloads=[];let stagedUpdate;
const appWindows=new Map();
function remoteContentsAllowed(contents){return contents===view?.webContents||[...appWindows.values()].some(w=>!w.isDestroyed()&&w.webContents===contents)}
const prefsFile=path.join(dataDir,'settings.json');
let prefs={devices:[{ip:'192.168.0.218',name:'桌面 Cubic'}],active:'192.168.0.218',updateFeed:'',uiLanguage:'zh-CN',autoSerialLog:true,holopetLocalSync:true,autoCompanion:true,serviceAutoStart:{},mirror:{monitor:1,region:'',fit:'stretch',fps:8,quality:65}};
try{prefs={...prefs,...JSON.parse(fs.readFileSync(prefsFile,'utf8'))}}catch{}
const dialog=localizedDialog(systemDialog,()=>prefs.uiLanguage||'zh-CN');
const savePrefs=()=>{fs.writeFileSync(prefsFile+'.tmp',JSON.stringify(prefs,null,2));fs.renameSync(prefsFile+'.tmp',prefsFile)};
const emit=(type,value)=>{if(type==='serial-state')serial=value;if(win&&!win.isDestroyed())win.webContents.send('desktop-event',{type,value})};
const log=message=>{const line=new Date().toLocaleString('sv-SE')+' '+message;fs.appendFileSync(logFile,line+'\n');emit('log',line)};
const delay=ms=>new Promise(r=>setTimeout(r,ms));
function helper(){
 if(native&&!native.killed)return native;
 native=spawn(path.join(__dirname,'native','HoloNative.exe'),[],{windowsHide:true,stdio:['pipe','pipe','pipe']});
 readline.createInterface({input:native.stdout}).on('line',line=>{try{const d=JSON.parse(line);if(d.id){const w=nativeWait.get(d.id);if(w){clearTimeout(w.timer);nativeWait.delete(d.id);d.ok?w.resolve(d.value):w.reject(Error(d.error))}}else{if(d.type==='serial-error')serialMonitor.lost(d.text,d.port);if(d.type==='serial'){serialMonitor.feed(d.text,d.port);serialWifi.feed(d.text);}if(d.type==='service-log')log(d.text);else if(d.type!=='serial'||serialMonitor.connection?.address===prefs.active)emit(d.type,d.text)}}catch{}});
 native.stderr.on('data',d=>log('本机工具：'+d.toString().slice(0,1000)));
 const child=native; const failed=()=>{if(native!==child)return;native=null;serialMonitor.lost('本机工具已退出');for(const w of nativeWait.values()){clearTimeout(w.timer);w.reject(Error('本机工具已退出'))}nativeWait.clear();emit('serial-state',null)};
 native.on('exit',failed);native.on('error',failed);native.stdin.on('error',failed);
 native.stdin.write(JSON.stringify({id:0,command:'monitor-config',data:{initial:true,bindings:prefs.performanceMonitor?.bindings||{}}})+'\n');
 return native;
}
function nativeCall(command,data={}){return new Promise((resolve,reject)=>{const process=helper(),id=++nativeSeq;const timer=setTimeout(()=>{nativeWait.delete(id);reject(Error('本机操作超时'))},60000);nativeWait.set(id,{resolve,reject,timer});process.stdin.write(JSON.stringify({id,command,data})+'\n')})}
function monitorCamel(value){if(Array.isArray(value))return value.map(monitorCamel);if(value&&typeof value==='object')return Object.fromEntries(Object.entries(value).map(([key,v])=>[key.charAt(0).toLowerCase()+key.slice(1),monitorCamel(v)]));return value;}
function browserState(){if(!view||view.webContents.isDestroyed())return;const wc=view.webContents;emit('browser',{url:wc.getURL(),title:wc.getTitle(),loading:wc.isLoading(),back:wc.navigationHistory.canGoBack(),forward:wc.navigationHistory.canGoForward()})}
const webUrl=url=>{try{return ['http:','https:','blob:','about:'].includes(new URL(url).protocol)}catch{return false}};
function sizeView(){if(!view||!win)return;const [w,h]=win.getContentSize();const b=viewBounds;view.setBounds({x:Math.max(0,b.x),y:Math.max(100,b.y),width:Math.max(1,Math.min(b.width,w-b.x)),height:Math.max(1,Math.min(b.height,h-b.y))});view.setVisible(remoteVisible)}
function ensureView(){
 if(view)return view;
 const ses=session.fromPartition('persist:device-browser');
 ses.setPermissionRequestHandler((_wc,permission,cb)=>cb(permission==='fullscreen'));
 if(!ses.holoDownloads){ses.holoDownloads=true;ses.on('will-download',(_event,item)=>{const id=crypto.randomUUID();const task={id,name:item.getFilename(),received:0,total:item.getTotalBytes(),state:'下载中'};downloads.unshift(task);emit('downloads',downloads);item.on('updated',()=>{task.received=item.getReceivedBytes();task.total=item.getTotalBytes();emit('downloads',downloads)});item.once('done',(_e,state)=>{task.state=state==='completed'?'已完成':state==='cancelled'?'已取消':'下载中断';task.path=item.getSavePath();emit('downloads',downloads);log('下载 '+task.name+' '+task.state)})});}
 view=new WebContentsView({webPreferences:{session:ses,preload:path.join(__dirname,'web-preload.cjs'),contextIsolation:true,nodeIntegration:false,sandbox:true,webSecurity:true,spellcheck:false}});
 win.contentView.addChildView(view);view.setVisible(false);
 const wc=view.webContents;
 wc.on('did-finish-load',()=>{try{if(new URL(wc.getURL()).pathname==='/main')wc.insertCSS('.dash-sidebar{display:none!important}.dashboard-frame{grid-template-columns:minmax(0,1fr)!important;border-radius:0!important;margin:0!important;max-width:none!important}.view{padding-top:0!important}').catch(()=>{})}catch{}});
 wc.on('will-navigate',(e,url)=>{if(!webUrl(url)){e.preventDefault();return}const target=new URL(url);if(target.origin==='http://'+prefs.active&&target.pathname!=='/main'&&!target.pathname.startsWith('/apps/launcher/')){e.preventDefault();(async()=>{const s=await device.request(prefs.active);if(s.current_webui&&s.current_route_base&&(target.pathname===s.current_route_base||target.pathname.startsWith(s.current_route_base+'/'))){await companions.observe(prefs.active,s);await openAppWindow({route:target.pathname+target.search+target.hash,title:s.current_app?.name,id:s.current_app?.id})}else await wc.loadURL(url)})().catch(error=>emit('browser-error',error.message));}});
 wc.on('will-redirect',(e,url)=>{if(!webUrl(url))e.preventDefault()});
 wc.setWindowOpenHandler(({url})=>{if(webUrl(url))wc.loadURL(url).catch(e=>emit('browser-error',e.message));return {action:'deny'}});
 ['did-start-loading','did-stop-loading','did-navigate','did-navigate-in-page','page-title-updated'].forEach(event=>wc.on(event,browserState));
 wc.on('did-fail-load',(_e,code,description,url,mainFrame)=>{if(mainFrame&&code!==-3){emit('browser-error',description);log('页面加载失败 '+code+' '+url)}});
 wc.on('render-process-gone',(_e,d)=>{log('设备页面进程退出 '+d.reason);emit('browser-error','设备页面进程已退出，请刷新')});
 return view;
}
async function openWeb(route,address=prefs.active){const origin='http://'+device.host(address);const url=new URL(route,origin);if(url.origin!==origin)throw Error('页面必须来自当前设备');const browser=ensureView();remoteVisible=true;sizeView();await browser.webContents.loadURL(url.href);browserState();return true}
async function openAppWindow({route,title,id,address=prefs.active},service=false){
 const key=service?'pc:'+id:address+':'+(id||route);
 const origin=service?'http://127.0.0.1:'+definitions.find(d=>d.id===id)?.port:'http://'+device.host(address);
 const url=new URL(route,origin);if(url.origin!==origin)throw Error('应用页面地址无效');
 let popup=appWindows.get(key);
 if(popup&&!popup.isDestroyed()){if(popup.isMinimized())popup.restore();popup.show();popup.focus();if(popup.webContents.getURL()!==url.href)await popup.loadURL(url.href);return true;}
 ensureView(); // Share the isolated browser session and download handling.
 popup=new BrowserWindow({parent:win,width:1000,height:740,minWidth:640,minHeight:440,show:false,autoHideMenuBar:true,title:String(title||'应用控制')+' · holocubic控制台',icon:path.join(__dirname,'assets','app.png'),webPreferences:{session:session.fromPartition('persist:device-browser'),preload:path.join(__dirname,'web-preload.cjs'),contextIsolation:true,nodeIntegration:false,sandbox:true,webSecurity:true,spellcheck:false}});
 appWindows.set(key,popup);popup.removeMenu();
 popup.webContents.setWindowOpenHandler(({url})=>{if(webUrl(url))popup.loadURL(url).catch(e=>log('应用窗口：'+e.message));return {action:'deny'}});
 popup.webContents.on('will-navigate',(e,url)=>{if(!webUrl(url))e.preventDefault()});
 popup.webContents.on('will-redirect',(e,url)=>{if(!webUrl(url))e.preventDefault()});
 popup.webContents.on('before-input-event',(e,input)=>{if(input.type!=='keyDown')return;const history=popup.webContents.navigationHistory;if(input.key==='F5'){e.preventDefault();popup.webContents.reload()}else if(input.alt&&input.key==='ArrowLeft'&&history.canGoBack()){e.preventDefault();history.goBack()}else if(input.alt&&input.key==='ArrowRight'&&history.canGoForward()){e.preventDefault();history.goForward()}});
 popup.on('closed',()=>appWindows.delete(key));popup.once('ready-to-show',()=>{popup.show();popup.focus()});
 try{await popup.loadURL(url.href);popup.show();popup.focus();return true}catch(e){popup.close();throw Error('应用控制窗口打开失败：'+e.message)}
}
async function stagePackage(file){const result=await nativeCall('stage-update',{path:file,current:__dirname,version:app.getVersion()});stagedUpdate=result;log('更新包已就绪 v'+result.version);return {version:result.version}}
const companions=new Companions({nativeCall,prefs:()=>prefs,savePrefs,emit,log,dataDir,appDir:__dirname});
const aiAccount=new AiAccount({dataDir,openExternal:url=>shell.openExternal(url),onChange:()=>holopetAI.syncAccount()});
const aiLocalMonitor=new LocalAiMonitor({enabled:()=>prefs.holopetLocalSync!==false,log,account:aiAccount});
const holopetAI=new HolopetAI({appDir:__dirname,dataDir,executable:process.execPath,start:()=>companions.start('holopet'),log,localMonitor:aiLocalMonitor,account:aiAccount});
const fileModule=createFiles({BrowserWindow,dialog,shell,window:()=>win,emit,dataDir,prefs:()=>prefs});
const iconCache=new Map();
const serialMonitor=new SerialMonitor({nativeCall,prefs:()=>prefs,savePrefs,emit,log,dataDir,isProvisioning:()=>serialWifi.active});
async function openSerial(data){return serialMonitor.open(data)}
function addDiscovered(found){let changed=false;for(const d of found){const existing=prefs.devices.find(x=>x.ip===d.ip);if(existing){existing.online=true;existing.lastSeen=Date.now();}else{prefs.devices.push({ip:d.ip,name:d.name,online:true,lastSeen:Date.now()});changed=true}}if(changed){savePrefs();emit('devices',prefs)}return changed}
const serialWifi=new SerialWifi({nativeCall,open:openSerial,close:()=>serialMonitor.closePort(),status:()=>serial,emit,onConnected:async address=>{const d=await device.probe(address,5000);addDiscovered([d]);await serialMonitor.bind(d.ip);emit('serial-wifi-device',{ip:d.ip});}});
let companionPollBusy=false,companionTimer;
async function pollCompanion(){if(companionPollBusy||!prefs.active||prefs.autoCompanion===false)return;companionPollBusy=true;try{const address=prefs.active,s=await device.request(address);if(address===prefs.active)await companions.observe(address,s)}catch{}finally{companionPollBusy=false}}
const browserLayout=createBrowserBackdrop({getView:()=>view,isVisible:()=>remoteVisible,setVisible:visible=>{remoteVisible=visible;sizeView()},setBounds:bounds=>{viewBounds=Object.fromEntries(Object.entries(bounds).map(([k,v])=>[k,Math.round(v)]))},emit});
const handlers={
 ...fileModule.handlers,
 setUiLanguage:({language})=>{if(!uiLanguages.includes(language))throw Error('Unsupported language');prefs.uiLanguage=language;savePrefs();emit('ui-language',language);fileModule.setLanguage(language);return language;},
 renameDevice:({ip,name})=>{const d=prefs.devices.find(x=>x.ip===ip);if(!d)throw Error('设备不存在');name=String(name||'').trim();if(!name||name.length>50)throw Error('设备名称不能为空且不能超过 50 个字符');d.name=name;savePrefs();emit('devices',prefs);return prefs;},
 init:()=>({prefs,version:app.getVersion(),logPath:logFile}),
 api:async({route,method='GET',body,address=prefs.active})=>{try{const r=await device.request(address,route,method,body);if(method!=='GET')log(method+' '+route.split('?')[0]+' 成功');if(route==='/api/system/state'&&address===prefs.active)companions.observe(address,r).catch(e=>log(e.message));return r}catch(e){log(method+' '+route.split('?')[0]+' 失败：'+e.message);throw e}},
 appIcons:async({apps,address=prefs.active})=>{const list=Array.isArray(apps)?apps.slice(0,100):[],result={};for(let i=0;i<list.length;i+=3){await Promise.all(list.slice(i,i+3).map(async a=>{const key=address+'|'+a.id+'|'+a.version;let entry=iconCache.get(key);if(!entry||Date.now()-entry.time>300000){entry={value:await device.appIcon(address,a.id),time:Date.now()};iconCache.set(key,entry)}result[a.id]=entry.value}))}return result},
 prepareApp:({id,address=prefs.active})=>companions.prepare(address,id),
 configureCompanion:async({address=prefs.active})=>{const s=await device.request(address);await companions.observe(address,s,true);return true},
 probe:({ip})=>device.probe(ip,3500),
 saveDevice:async({ip,name,verify=true})=>{ip=device.host(ip);if(verify)await device.probe(ip,3500);const old=prefs.devices.find(d=>d.ip===ip);if(old)old.name=String(name||old.name).slice(0,50);else prefs.devices.push({ip,name:String(name||'Cubic '+ip).slice(0,50)});prefs.active=ip;savePrefs();serialMonitor.selected();log('添加设备 '+ip);return prefs},
 selectDevice:({ip})=>{if(!prefs.devices.some(d=>d.ip===ip))throw Error('设备不存在');prefs.active=ip;savePrefs();serialMonitor.selected();if(view){view.webContents.stop();view.webContents.close();view=null}remoteVisible=false;return prefs},
 removeDevice:({ip})=>{if(prefs.devices.length<=1)throw Error('请至少保留一个设备');prefs.devices=prefs.devices.filter(d=>d.ip!==ip);if(prefs.active===ip)prefs.active=prefs.devices[0].ip;savePrefs();return prefs},
 scan:async()=>{if(scanController)throw Error('正在扫描');scanController=new AbortController();try{const result=await device.scan(p=>{addDiscovered(p.found);emit('scan',p)},scanController.signal);log('局域网扫描完成，已自动添加 '+result.found.length+' 台设备');return {...result,prefs}}finally{scanController=null}},
 cancelScan:()=>{scanController?.abort();return true},
 openWeb:({route,address=prefs.active})=>openWeb(route,address),
 openAppWindow:data=>openAppWindow(data),
 browserLayout,
 browserAction:async({action})=>{if(!view)return false;const wc=view.webContents;if(action==='back'&&wc.navigationHistory.canGoBack())wc.navigationHistory.goBack();else if(action==='forward'&&wc.navigationHistory.canGoForward())wc.navigationHistory.goForward();else if(action==='reload')wc.reload();else if(action==='stop')wc.stop();browserState();return true},
 logs:()=>fs.readFileSync(logFile,'utf8').slice(-150000),
 exportLogs:async()=>{const r=await dialog.showSaveDialog(win,{defaultPath:'holocubic-日志.txt'});if(!r.canceled){fs.copyFileSync(logFile,r.filePath);return true}return false},
 downloads:()=>downloads,
 revealDownload:({id})=>{const task=downloads.find(d=>d.id===id&&d.state==='已完成');if(task?.path)shell.showItemInFolder(task.path);return true},
 serialStatus:async()=>({ports:await nativeCall('ports'),connection:serialMonitor.connection,monitor:serialMonitor.snapshot()}),
 serialLogStatus:()=>serialMonitor.snapshot(),
 serialClear:()=>{serialMonitor.text='';return true;},
 serialLogConfigure:({enabled})=>serialMonitor.setEnabled(enabled),
 serialLogFolder:async()=>{fs.mkdirSync(serialMonitor.root,{recursive:true});const error=await shell.openPath(serialMonitor.root);if(error)throw Error(error);return true;},
 serialOpen:async data=>{serial=await openSerial(data);log('串口连接 '+serial.port+' '+serial.baud);return serial},
 serialClose:async()=>{serialWifi.cancel();await serialMonitor.closePort();log('串口已断开');return null},
 serialWifiStatus:()=>({...serialWifi.last,active:serialWifi.active}),
 serialWifiScan:data=>serialWifi.run('scan',data),
 serialWifiProvision:data=>serialWifi.run('provision',data),
 serialWifiCancel:()=>serialWifi.cancel(),
 serialWrite:({text})=>{if(typeof text!=='string'||text.length>8192)throw Error('发送内容过长');return nativeCall('write',{text})},
 pcStatus:()=>companions.snapshot(),
 holopetAiStatus:()=>holopetAI.status(),
 holopetAccountConnect:async({type}={})=>{await aiAccount.connect(type);return holopetAI.status()},
 holopetAccountRefresh:async()=>{await aiAccount.refresh(true);await holopetAI.syncAccount();return holopetAI.status()},
 holopetAccountDisconnect:async()=>{await aiAccount.disconnect();await holopetAI.syncAccount();return holopetAI.status()},
 holopetAccountCancel:async()=>{await aiAccount.cancelLogin();return holopetAI.status()},
 holopetAccountReopen:async()=>{await aiAccount.reopenLogin();return holopetAI.status()},
 holopetAiLocalConfigure:async({enabled})=>{prefs.holopetLocalSync=!!enabled;savePrefs();if(enabled){aiLocalMonitor.start();await aiLocalMonitor.poll()}else aiLocalMonitor.stop();return holopetAI.status()},
 holopetAiInstall:()=>holopetAI.install(),
 holopetAiTest:()=>holopetAI.test(),
 holopetAiExtension:async()=>{const error=await shell.openPath(holopetAI.extension);if(error)throw Error(error);return true},
 holopetAiHelp:async()=>{const error=await shell.openPath(path.join(__dirname,'integrations','使用说明.md'));if(error)throw Error(error);return true},
 serviceStart:({id})=>companions.start(id),
 serviceStop:({id})=>companions.stop(id),
 serviceAutoStart:({id,enabled})=>{if(!definitions.some(d=>d.id===id))throw Error('未知服务');prefs.serviceAutoStart={...prefs.serviceAutoStart,[id]:!!enabled};savePrefs();return true},
 autoCompanion:({enabled})=>{prefs.autoCompanion=!!enabled;savePrefs();if(enabled)pollCompanion();return prefs.autoCompanion},
 pcServicePage:async({id})=>{const d=definitions.find(d=>d.id===id);if(!d)throw Error('未知服务');await companions.start(id);return openAppWindow({route:'/',title:d.name,id},true)},
 mirrorStatus:()=>nativeCall('mirror-status'),
 mirrorConfigure:async data=>{const value=await nativeCall('mirror-config',data);prefs.mirror=data;savePrefs();return value},
 monitorSnapshot:async()=>monitorCamel(await nativeCall('monitor-snapshot')),
 monitorConfigure:async({bindings})=>{if(!bindings||typeof bindings!=='object'||Array.isArray(bindings))throw Error('传感器映射格式不正确');const snapshot=monitorCamel(await nativeCall('monitor-config',{bindings}));prefs.performanceMonitor={...prefs.performanceMonitor,bindings:snapshot.settings.bindings};savePrefs();return snapshot;},
 monitorElevate:()=>nativeCall('monitor-elevate'),
 saveFeed:({url})=>{if(url){const u=new URL(url);if(u.protocol!=='https:'||u.username||u.password)throw Error('更新源必须是 HTTPS 地址')}prefs.updateFeed=url||'';savePrefs();return true},
 checkUpdate:async()=>{if(!prefs.updateFeed)throw Error('请先填写更新源地址，或直接导入本地更新包');const response=await fetch(prefs.updateFeed,{signal:AbortSignal.timeout(15000)});if(!response.ok)throw Error('更新源 HTTP '+response.status);const m=await response.json();if(m.appId!=='holocubic-console'||!/^\d+\.\d+\.\d+$/.test(m.version)||!/^https:\/\//.test(m.url)||!/^[a-f0-9]{64}$/i.test(m.sha256))throw Error('更新清单格式不正确');const a=m.version.split('.').map(Number),b=app.getVersion().split('.').map(Number);m.available=a.some((v,i)=>v>b[i]&&a.slice(0,i).every((n,j)=>n===b[j]));stagedUpdate={manifest:m};return {version:m.version,notes:String(m.notes||''),available:m.available}},
 downloadUpdate:async()=>{const m=stagedUpdate?.manifest;if(!m?.available)throw Error('请先检查更新');const r=await fetch(m.url,{signal:AbortSignal.timeout(180000)});if(!r.ok||!r.url.startsWith('https://'))throw Error('更新包下载失败');const file=path.join(dataDir,'update-download.zip');const stream=fs.createWriteStream(file);const hash=crypto.createHash('sha256');let size=0;try{for await(const chunk of r.body){size+=chunk.length;if(size>300e6)throw Error('更新包过大');hash.update(chunk);if(!stream.write(chunk))await new Promise(resolve=>stream.once('drain',resolve));emit('update-download',{received:size,total:Number(r.headers.get('content-length')||0)})}await new Promise((resolve,reject)=>{stream.on('error',reject);stream.end(resolve)});if(hash.digest('hex').toLowerCase()!==m.sha256.toLowerCase())throw Error('更新包校验失败');return await stagePackage(file)}catch(e){stream.destroy();throw e}},
 importUpdate:async()=>{const r=await dialog.showOpenDialog(win,{title:'选择 holocubic 控制台更新包',properties:['openFile'],filters:[{name:'更新包',extensions:['zip']}]});if(r.canceled)return null;return stagePackage(r.filePaths[0])},
 applyUpdate:async()=>{if(!stagedUpdate?.stage)throw Error('请先准备更新包');const {response}=await dialog.showMessageBox(win,{type:'question',title:'更新 holocubic 控制台',message:'安装 v'+stagedUpdate.version+' 并重启软件？',detail:'设备列表和软件设置会保留，上一版程序会自动备份。',buttons:['安装并重启','取消'],defaultId:0,cancelId:1});if(response!==0)return false;const installer=path.join(dataDir,'HoloUpdater.exe');fs.copyFileSync(path.join(__dirname,'native','HoloNative.exe'),installer);const job=path.join(dataDir,'update-job.json');fs.writeFileSync(job,JSON.stringify({current:__dirname,stage:stagedUpdate.stage,exe:process.execPath,pid:process.pid,children:[native?.pid,companions.music?.pid].filter(Number.isInteger)}));spawn(installer,['--apply-update',job],{detached:true,windowsHide:true,stdio:'ignore'}).unref();setTimeout(()=>app.quit(),100);return true}
};
if(!app.requestSingleInstanceLock())app.quit();else{
 app.on('second-instance',()=>{if(win){if(win.isMinimized())win.restore();win.show();win.focus()}});
 app.whenReady().then(()=>{
  log('holocubic 控制台 v'+app.getVersion()+' 启动');
  win=new BrowserWindow({width:1500,height:960,minWidth:1180,minHeight:720,frame:false,show:false,backgroundColor:'#edf1f6',title:'holocubic控制台',icon:path.join(__dirname,'assets','app.png'),webPreferences:{preload:path.join(__dirname,'preload.cjs'),contextIsolation:true,nodeIntegration:false,sandbox:true}});
  win.removeMenu();win.webContents.setWindowOpenHandler(()=>({action:'deny'}));win.webContents.on('will-navigate',e=>e.preventDefault());
  win.webContents.on('render-process-gone',(_e,d)=>log('主界面异常 '+JSON.stringify(d)));win.webContents.on('did-finish-load',()=>log('主界面加载完成'));
  win.loadFile(path.join(__dirname,'index.html'));win.once('ready-to-show',()=>{win.show();win.focus()});
  aiAccount.start();if(prefs.holopetLocalSync!==false)aiLocalMonitor.start();serialMonitor.start();companions.autoStart().catch(e=>log(e.message));companionTimer=setInterval(pollCompanion,4500);
  let closingFiles=false,closeConfirmed=false;win.on('close',e=>{if(closeConfirmed||!fileModule.hasPending())return;e.preventDefault();if(closingFiles)return;closingFiles=true;fileModule.confirmClose().then(ok=>{closingFiles=false;if(ok){closeConfirmed=true;win.close();}});});
  win.on('resize',sizeView);win.on('maximize',()=>emit('maximized',true));win.on('unmaximize',()=>emit('maximized',false));
  win.on('closed',()=>{for(const popup of appWindows.values())if(!popup.isDestroyed())popup.destroy();if(view&&!view.webContents.isDestroyed())view.webContents.close();view=null;win=null});
 });
 ipcMain.handle('desktop-call',async(event,method,data)=>{const editorResult=await fileModule.editorCall(event,method,data||{});if(editorResult!==undefined)return editorResult;if(!win||event.sender!==win.webContents||event.senderFrame!==win.webContents.mainFrame)return {ok:false,error:'拒绝访问'};try{if(!Object.hasOwn(handlers,method))throw Error('未知操作');return {ok:true,value:await handlers[method](data||{})}}catch(e){return {ok:false,error:translate(e.message,prefs.uiLanguage)}}});
 ipcMain.on('window-action',(e,a)=>{if(!win||e.sender!==win.webContents)return;if(a==='minimize')win.minimize();if(a==='maximize')win.isMaximized()?win.unmaximize():win.maximize();if(a==='close')win.close()});
 ipcMain.on('web-prompt',(event,data)=>{if(!remoteContentsAllowed(event.sender)||promptWindow){event.returnValue=null;return}promptEvent=event;promptData={message:String(data.message).slice(0,2000),value:String(data.value).slice(0,4000)};promptWindow=new BrowserWindow({parent:BrowserWindow.fromWebContents(event.sender)||win,modal:true,width:540,height:290,resizable:false,autoHideMenuBar:true,title:'网页输入 · holocubic控制台',webPreferences:{preload:path.join(__dirname,'prompt-preload.cjs'),sandbox:true,contextIsolation:true,nodeIntegration:false}});promptWindow.loadFile(path.join(__dirname,'prompt.html'));promptWindow.on('closed',()=>{if(promptEvent)promptEvent.returnValue=null;promptEvent=null;promptWindow=null})});
 ipcMain.handle('prompt-init',e=>e.sender===promptWindow?.webContents?promptData:null);
 ipcMain.on('prompt-done',(e,value)=>{if(e.sender!==promptWindow?.webContents)return;if(promptEvent)promptEvent.returnValue=value===null?null:String(value).slice(0,4000);promptEvent=null;promptWindow.close()});
 app.on('before-quit',()=>{aiAccount.close();aiLocalMonitor.stop();serialMonitor.stop();fileModule.manager.close();clearInterval(companionTimer);serialWifi.cancel();companions.close();scanController?.abort();native?.stdin.end();smtc?.kill()});app.on('window-all-closed',()=>app.quit());
}


