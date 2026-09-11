'use strict';
importScripts('providers.js');
const states=new Set(['idle','thinking','working','building','notification','done','error','sleeping']);
const active=new Set(['thinking','working','building']);
let serial=Promise.resolve();
function queue(fn){const task=serial.then(fn,fn);serial=task.catch(()=>{});return task}
async function publish(status){let error='';try{const response=await fetch('http://127.0.0.1:17321/event',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(status),signal:AbortSignal.timeout(2000)});if(!response.ok)throw Error('HTTP '+response.status)}catch(e){error='无法连接 Holopet，请启动控制台的 Holopet 服务。'}await chrome.storage.local.set({last:{...status,received_at:Date.now()},connectionError:error});await chrome.action.setBadgeText({text:error?'!':status.state==='thinking'?'…':''});await chrome.action.setBadgeBackgroundColor({color:error?'#d34444':'#20a68a'});return {ok:!error,error}}
async function activity(message,sender,manual=false){
 let provider=sender.tab?HoloProviders.find(new URL(sender.url||sender.tab.url).hostname):null;
 if(!manual&&(!provider||provider.id!==message.provider))return {ok:false};
 const settings=await chrome.storage.local.get({enabled:true});if(!manual&&!settings.enabled)return {ok:false};
 if(!states.has(message.state))return {ok:false};
 const values=await chrome.storage.session.get({tabs:{}}),tabs=values.tabs,now=Date.now();
 const key=manual?'manual':String(sender.tab.id);
 tabs[key]={state:message.state,event:String(message.event||'Status').replace(/[^a-zA-Z]/g,'').slice(0,40),source:manual?'ai-web-manual':provider.id+'-web',project:manual?'手动同步':provider.name,tool:'',model:'',session:'browser-'+key,sent_at:now};
 for(const id of Object.keys(tabs))if(now-tabs[id].sent_at>60000)delete tabs[id];
 await chrome.storage.session.set({tabs});
 const busy=Object.values(tabs).filter(s=>active.has(s.state)).sort((a,b)=>b.sent_at-a.sent_at);
 const current=manual?tabs[key]:(busy[0]||tabs[key]);
 return publish(current);
}
chrome.runtime.onMessage.addListener((message,sender,reply)=>{
 if(message?.kind==='activity'){queue(()=>activity(message,sender)).then(reply).catch(()=>reply({ok:false}));return true}
 if(sender.id===chrome.runtime.id&&!sender.tab&&sender.url?.startsWith(chrome.runtime.getURL(''))){
  if(message?.kind==='manual'){queue(()=>activity({...message,event:'Manual'},sender,true)).then(reply).catch(()=>reply({ok:false}));return true}
  if(message?.kind==='health'){fetch('http://127.0.0.1:17321/health',{signal:AbortSignal.timeout(1500)}).then(r=>reply({ok:r.ok})).catch(()=>reply({ok:false}));return true}
 }
});
chrome.tabs.onRemoved.addListener(id=>queue(async()=>{const {tabs={}}=await chrome.storage.session.get('tabs');const closed=tabs[id];if(!closed)return;delete tabs[id];await chrome.storage.session.set({tabs});const busy=Object.values(tabs).filter(s=>active.has(s.state)).sort((a,b)=>b.sent_at-a.sent_at);if(busy.length)await publish(busy[0]);else if(active.has(closed.state))await publish({...closed,state:'idle',event:'TabClosed',sent_at:Date.now()})}));
chrome.alarms.create('stale',{periodInMinutes:0.5});
chrome.storage.onChanged.addListener((changes,area)=>{if(area!=='local'||changes.enabled?.newValue!==false)return;queue(async()=>{const {tabs={}}=await chrome.storage.session.get('tabs');const busy=Object.values(tabs).find(s=>active.has(s.state));await chrome.storage.session.set({tabs:{}});if(busy)await publish({...busy,state:'idle',event:'Paused',sent_at:Date.now()})})});
chrome.alarms.onAlarm.addListener(alarm=>{if(alarm.name!=='stale')return;queue(async()=>{const {tabs={}}=await chrome.storage.session.get('tabs');let expired;for(const id of Object.keys(tabs))if(Date.now()-tabs[id].sent_at>60000){if(active.has(tabs[id].state))expired=tabs[id];delete tabs[id]}await chrome.storage.session.set({tabs});if(expired&&!Object.values(tabs).some(s=>active.has(s.state)))await publish({...expired,state:'idle',event:'SignalLost',sent_at:Date.now()})})});
