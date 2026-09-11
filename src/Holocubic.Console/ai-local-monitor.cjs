'use strict';
const fs=require('node:fs'),path=require('node:path'),os=require('node:os');
const {StringDecoder}=require('node:string_decoder');
const {publish}=require('./integrations/ai-status.cjs');
const {usageFor,windowOf}=require('./ai-account.cjs');
const MAX_READ=1024*1024,MAX_AGE=5*60*1000;
const clip=(v,n=80)=>typeof v==='string'?v.slice(0,n):'';
function eventFromRecord(record,meta={}){
 const p=record?.payload;if(!p||typeof p!=='object')return null;
 if(record.type==='session_meta'){meta.session=clip(p.id||p.session_id);meta.project=clip(String(p.cwd||'').replace(/[\\/]+$/,'').split(/[\\/]/).pop(),40);return null}
 if(record.type==='turn_context'||record.type==='event_msg'&&p.type==='thread_settings_applied'){const settings=p.thread_settings||p;meta.model=clip(settings.model,40)||meta.model;meta.effort=clip(settings.effort||settings.reasoning_effort,16)||meta.effort;return null}
 if(record.type==='event_msg'&&p.type==='token_count'){
  const info=p.info,last=info?.last_token_usage;const used=last?.total_tokens,window=info?.model_context_window;
  if(typeof used==='number'&&used>=0&&typeof window==='number'&&window>0)meta.context={used_tokens:used,window_tokens:window,percent:Math.min(100,used/window*100)};
  const rate=p.rate_limits;if(rate?.limit_id){meta.limits||={};meta.limits[rate.limit_id]={primary:windowOf(rate.primary),secondary:windowOf(rate.secondary)}}
  if(meta.lastState){const next={...meta.lastState,context:meta.context||null,model:meta.model||'',effort:meta.effort||'',sent_at:Date.parse(record.timestamp)};if(Number.isFinite(next.sent_at))return next}return null;
 }
 let state,event,tool='';
 if(record.type==='event_msg'){
  const type=p.type;
  if(['task_started','turn_started','user_message'].includes(type)){state='thinking';event='UserPromptSubmit'}
  else if(['task_complete','turn_complete'].includes(type)){state='done';event='Stop'}
  else if(['turn_aborted','task_aborted'].includes(type)){state='idle';event='Interrupt'}
  else if(['exec_approval_request','apply_patch_approval_request','permission_request'].includes(type)){state='notification';event='PermissionRequest'}
  else if(['stream_error','error'].includes(type)){state='error';event='TaskError'}
  else if(type==='item_completed'){
   const item=p.item||{};
   if(item.type==='Reasoning'){state='thinking';event='Reasoning'}
   else if(item.type==='CommandExecution'&&typeof item.exit_code==='number'&&item.exit_code!==0){state='error';event='ToolError'}
   else if(item.type==='AgentMessage'&&item.phase==='final_answer'){state='done';event='Stop'}
  }
 }else if(record.type==='response_item'){
  if(p.type==='reasoning'){state='thinking';event='Reasoning'}
  else if(['function_call','custom_tool_call','local_shell_call','web_search_call'].includes(p.type)){tool=clip(p.name||p.type,48);state=/request_user_input/.test(tool)?'notification':'working';event=state==='notification'?'PermissionRequest':'PreToolUse'}
  else if(['function_call_output','custom_tool_call_output'].includes(p.type)){state='thinking';event='PostToolUse'}
  else if(p.type==='message'&&p.role==='assistant'&&(p.phase==='final_answer'||p.channel==='final')){state='done';event='Stop'}
 }
 const timestamp=Date.parse(record.timestamp);if(!state||!Number.isFinite(timestamp))return null;
 const next={state,event,source:'openai-local-log',session:meta.session||'',project:meta.project||'',model:meta.model||'',effort:meta.effort||'',context:meta.context||null,tool,sent_at:timestamp};meta.lastState=next;return next;
}
class LocalAiMonitor{
 constructor({configHome,enabled=()=>true,send=publish,readBridge,now=()=>Date.now(),log=()=>{},account}={}){
  this.root=path.join(configHome||process.env.CODEX_HOME||path.join(os.homedir(),'.codex'),'sessions');Object.assign(this,{enabled,send,now,log});
  this.readBridge=readBridge||async function(){const r=await fetch('http://127.0.0.1:17321/status',{signal:AbortSignal.timeout(900)});if(!r.ok)throw Error('Holopet 服务未启动');return r.json()};
  this.account=account;this.entries=new Map();this.lastScan=0;this.lastCheck=0;this.lastSent=null;this.lastDetected=null;this.error='';this.busy=false;this.running=false;
 }
 snapshot(){return {enabled:this.enabled(),watching:this.running&&fs.existsSync(this.root),sessions:this.entries.size,lastDetected:this.lastDetected,lastSent:this.lastSent,error:this.error}}
 add(file){if(!file.endsWith('.jsonl')||this.entries.has(file))return;try{const stat=fs.statSync(file);if(stat.isFile())this.entries.set(file,{offset:0,buffer:'',decoder:new StringDecoder('utf8'),meta:{},mtime:stat.mtimeMs,initial:true,discard:false})}catch{}}
 scan(){this.lastScan=this.now();const found=[];const walk=dir=>{let files;try{files=fs.readdirSync(dir,{withFileTypes:true})}catch{return}for(const file of files){if(found.length>20000)break;const full=path.join(dir,file.name);if(file.isDirectory())walk(full);else if(file.isFile()&&file.name.endsWith('.jsonl'))try{found.push({file:full,time:fs.statSync(full).mtimeMs})}catch{}}};walk(this.root);found.sort((a,b)=>b.time-a.time);for(const f of found.slice(0,12))this.add(f.file);if(this.entries.size>24){const keep=new Set(found.slice(0,24).map(f=>f.file));for(const key of this.entries.keys())if(!keep.has(key))this.entries.delete(key)}}
 read(file,entry){
  const stat=fs.statSync(file);if(stat.size===entry.offset&&!entry.initial)return;entry.mtime=stat.mtimeMs;
  if(entry.initial){const fd=fs.openSync(file,'r');try{const head=Buffer.alloc(Math.min(256*1024,stat.size));fs.readSync(fd,head,0,head.length,0);const lines=head.toString('utf8').split('\n');lines.pop();for(const line of lines)try{eventFromRecord(JSON.parse(line),entry.meta)}catch{}}finally{fs.closeSync(fd)}}
  let skip=false;if(stat.size<entry.offset){entry.offset=0;entry.buffer='';entry.decoder=new StringDecoder('utf8');entry.discard=false;entry.last=null}
  if(stat.size-entry.offset>MAX_READ){entry.offset=Math.max(0,stat.size-512*1024);entry.buffer='';entry.decoder=new StringDecoder('utf8');entry.discard=false;skip=entry.offset>0}
  const size=stat.size-entry.offset;if(!size){entry.initial=false;return}const buffer=Buffer.alloc(size),fd=fs.openSync(file,'r');let count;try{count=fs.readSync(fd,buffer,0,size,entry.offset)}finally{fs.closeSync(fd)}entry.offset+=count;entry.initial=false;
  let text=entry.decoder.write(buffer.subarray(0,count));if(skip||entry.discard){const end=text.indexOf('\n');if(end<0){entry.discard=true;return}text=text.slice(end+1);entry.discard=false}
  const lines=(entry.buffer+text).split('\n');entry.buffer=lines.pop();if(entry.buffer.length>MAX_READ){entry.buffer='';entry.discard=true}
  for(const line of lines){if(!line.trim())continue;try{const state=eventFromRecord(JSON.parse(line),entry.meta);if(state&&(!entry.last||state.sent_at>=entry.last.sent_at))entry.last=state}catch{}}
 }
 async poll(){if(this.busy||!this.enabled())return;this.busy=true;try{
  if(!this.lastScan||this.now()-this.lastScan>30000)this.scan();
  for(const [file,entry]of this.entries)try{this.read(file,entry)}catch(e){if(e.code==='ENOENT')this.entries.delete(file);else this.error='无法读取本地任务日志'}
  const latest=[...this.entries.values()].filter(e=>e.last).map(e=>({...e.last,model:e.meta.model||e.last.model,effort:e.meta.effort||e.last.effort,context:e.meta.context||e.last.context,usage:usageFor({...e.meta.limits,...this.account?.limits},e.meta.model||e.last.model,this.now()),usage_summary:this.account?.stats?.summary||null,account_usage_at:this.account?.updatedAt||null})).sort((a,b)=>b.sent_at-a.sent_at);this.lastDetected=latest[0]||null;
  const fresh=latest.filter(s=>this.now()-s.sent_at<MAX_AGE&&s.sent_at<=this.now()+5000),candidate=fresh.find(s=>['thinking','working','notification','building'].includes(s.state))||fresh[0];
  if(!candidate)return;
  const key=candidate.session+'|'+candidate.event+'|'+candidate.sent_at+'|'+JSON.stringify(candidate.usage)+'|'+JSON.stringify(candidate.context)+'|'+candidate.account_usage_at;
  if(key===this.sentKey&&this.now()-this.lastCheck<10000)return;
  if(key===this.attemptKey&&this.now()-this.lastCheck<2500)return;
  this.lastCheck=this.now();this.attemptKey=key;
  let bridge;try{bridge=await this.readBridge()}catch{this.error='等待 Holopet 服务启动';return}
  // Do not let an old local event replace newer activity from a browser or a hook.
  this.account?.refresh();
  if(bridge.event!=='BridgeStart'&&Number(bridge.sent_at)>=candidate.sent_at&&(bridge.source!=='openai-local-log'||this.sentKey===key)){this.error='';if(bridge.source==='openai-local-log'){this.lastSent=bridge;this.sentKey=key}return}
  if(await this.send(candidate)){this.lastSent=candidate;this.sentKey=key;this.error=''}else this.error='状态发送失败，正在重试';
 }catch(e){this.error=e.message}finally{this.busy=false}}
 start(){if(this.timer)return;this.running=true;this.poll();this.timer=setInterval(()=>this.poll(),1000);this.timer.unref?.();try{this.watcher=fs.watch(this.root,{recursive:true,persistent:false},(_event,name)=>{if(name?.endsWith('.jsonl'))this.add(path.join(this.root,name))});this.watcher.on('error',()=>{})}catch{}}
 stop(){this.running=false;clearInterval(this.timer);this.timer=null;this.watcher?.close();this.watcher=null}
}
module.exports={LocalAiMonitor,eventFromRecord};
