'use strict';
const fs=require('node:fs'),path=require('node:path'),os=require('node:os'),readline=require('node:readline');
const {spawn}=require('node:child_process');
const numeric=v=>typeof v==='number'&&Number.isFinite(v)?v:null;
function windowOf(w){if(!w)return null;return {usedPercent:numeric(w.usedPercent??w.used_percent),windowDurationMins:numeric(w.windowDurationMins??w.window_minutes),resetsAt:numeric(w.resetsAt??w.resets_at)}}
function sanitizeLimits(result){const source=result.rateLimitsByLimitId||{[result.rateLimits?.limitId||'codex']:result.rateLimits};const limits={};for(const [id,v]of Object.entries(source)){if(!v)continue;limits[id]={limitId:id,limitName:typeof v.limitName==='string'?v.limitName.slice(0,64):null,primary:windowOf(v.primary),secondary:windowOf(v.secondary)}}return limits}
function usageFor(limits,model,now=Date.now()){
 const id=/spark|bengalfox/i.test(model)?'codex_bengalfox':'codex',limit=limits?.[id];if(!limit)return null;
 const windows=[limit.primary,limit.secondary].filter(Boolean),five=windows.find(w=>w.windowDurationMins===300),week=windows.find(w=>w.windowDurationMins===10080);
 const remaining=t=>{if(!t)return '--:--';const minutes=Math.max(0,Math.ceil((t*1000-now)/60000));return String(Math.floor(minutes/60)).padStart(2,'0')+':'+String(minutes%60).padStart(2,'0')};
 const date=t=>{if(!t)return '--/--';const d=new Date(t*1000);return String(d.getMonth()+1).padStart(2,'0')+'/'+String(d.getDate()).padStart(2,'0')};
 return {limit_id:id,five_hour_percent:five?.usedPercent??null,five_hour_resets_at:five?.resetsAt??null,five_hour_reset_text:remaining(five?.resetsAt),weekly_percent:week?.usedPercent??null,weekly_resets_at:week?.resetsAt??null,weekly_reset_text:date(week?.resetsAt)};
}
function findCodex(){const bin=path.join(process.env.LOCALAPPDATA||path.join(os.homedir(),'AppData','Local'),'OpenAI','Codex','bin');try{const candidates=fs.readdirSync(bin).map(name=>path.join(bin,name,'codex.exe')).filter(file=>fs.existsSync(file));candidates.sort((a,b)=>fs.statSync(b).mtimeMs-fs.statSync(a).mtimeMs);if(candidates.length)return candidates[0]}catch{}for(const dir of (process.env.PATH||'').split(path.delimiter)){const file=path.join(dir,'codex.exe');if(fs.existsSync(file))return file}return null}
function sanitizeUsage(result={}){
 const summary={};for(const key of ['lifetimeTokens','peakDailyTokens','longestRunningTurnSec','currentStreakDays','longestStreakDays'])summary[key]=numeric(result.summary?.[key]);
 const dailyUsageBuckets=Array.isArray(result.dailyUsageBuckets)?result.dailyUsageBuckets.filter(b=>/^\d{4}-\d{2}-\d{2}$/.test(b.startDate)&&numeric(b.tokens)!==null).slice(-366).map(b=>({startDate:b.startDate,tokens:b.tokens})):null;
 return {summary,dailyUsageBuckets};
}
function loginUrl(value){const url=new URL(value);if(url.protocol!=='https:'||!['auth.openai.com','auth0.openai.com','chatgpt.com','openai.com'].includes(url.hostname)||url.username||url.password)throw Error('登录页面地址无效');return url.href}
const errors={initialize:'账号接口初始化失败', 'account/read':'无法读取登录状态', 'account/login/start':'无法开始登录，请重试或使用设备码登录', 'account/login/cancel':'取消登录失败', 'account/logout':'断开连接失败', 'account/rateLimits/read':'额度暂不可用，将自动重试', 'account/usage/read':'使用统计暂不可用'};
class AiAccount{
 constructor({dataDir,openExternal=async()=>{},onChange=()=>{},executable,spawnProcess=spawn}={}){
  if(!dataDir)throw Error('需要控制台数据目录');
  // This profile is owned by Holocubic. Never borrow the desktop client's auth or configuration.
  this.configHome=path.join(path.resolve(dataDir),'chatgpt-account');Object.assign(this,{openExternal,onChange,executable,spawnProcess});
  this.limits={};this.stats=null;this.account=null;this.updatedAt=0;this.statsUpdatedAt=0;this.attemptedAt=0;this.error='';this.statsError='';this.waiters=new Map();this.seq=0;this.child=null;this.closed=false;this.generation=0;
 }
 hasLogin(){return fs.existsSync(path.join(this.configHome,'auth.json'))}
 snapshot(){return {connected:!!this.account,account:this.account,login:this.login?{type:this.login.type,userCode:this.login.userCode||null,startedAt:this.login.startedAt}:null,loading:!!this.pending||!!this.connecting,updatedAt:this.updatedAt,statsUpdatedAt:this.statsUpdatedAt,error:this.error,statsError:this.statsError,limits:this.limits,stats:this.stats,profilePath:this.configHome}}
 changed(){try{Promise.resolve(this.onChange(this.snapshot())).catch(()=>{})}catch{}}
 write(message){if(!this.child||this.child.stdin.destroyed)throw Error('账号接口未连接');this.child.stdin.write(JSON.stringify(message)+'\n')}
 rpc(method,params,timeout=20000){return new Promise((resolve,reject)=>{const id=++this.seq,timer=setTimeout(()=>{this.waiters.delete(id);reject(Error(errors[method]||'账号接口请求超时'))},timeout);this.waiters.set(id,{resolve,reject,timer,method});try{this.write({id,method,...(params===undefined?{}:{params})})}catch(error){clearTimeout(timer);this.waiters.delete(id);reject(error)}})}
 async ensure(){
  if(this.closed)throw Error('账号连接已关闭');if(this.ready)return;if(this.starting)return this.starting;
  this.starting=(async()=>{
   const executable=this.executable||findCodex();if(!executable)throw Error('未找到 Codex 运行组件，请安装 ChatGPT / Codex 客户端');
   const cache=path.join(this.configHome,'runtime'),temp=path.join(this.configHome,'tmp');fs.mkdirSync(cache,{recursive:true});fs.mkdirSync(temp,{recursive:true});
   const env={...process.env};for(const key of Object.keys(env))if(/^(CODEX_|OPENAI_|CHATGPT_|ELECTRON_RUN_AS_NODE$)/i.test(key))delete env[key];
   Object.assign(env,{CODEX_HOME:this.configHome,CODEX_SQLITE_HOME:cache,TMP:temp,TEMP:temp});
   const child=this.spawnProcess(executable,['app-server','--stdio','-c','cli_auth_credentials_store="file"','-c','check_for_update_on_startup=false'],{windowsHide:true,cwd:this.configHome,env,stdio:['pipe','pipe','pipe']});this.child=child;
   const fail=()=>{if(this.child!==child)return;this.child=null;this.ready=false;for(const w of this.waiters.values()){clearTimeout(w.timer);w.reject(Error('账号接口连接中断，请重试'))}this.waiters.clear();if(this.login){this.clearLogin();this.error='登录已中断，请重试'}this.changed()};
   child.on('error',fail);child.on('exit',fail);child.stdin.on('error',()=>{});child.stderr.on('data',()=>{});
   readline.createInterface({input:child.stdout}).on('line',line=>{let m;try{m=JSON.parse(line)}catch{return}if(m.id!=null&&!m.method){const w=this.waiters.get(m.id);if(!w)return;clearTimeout(w.timer);this.waiters.delete(m.id);m.error?w.reject(Error(errors[w.method]||'账号接口请求失败')):w.resolve(m.result||{});}else if(m.id!=null){try{this.write({id:m.id,error:{code:-32601,message:'This client supports account status only'}})}catch{}}else this.notification(m);});
   try{await this.rpc('initialize',{clientInfo:{name:'holocubic-console',title:'Holocubic Console',version:'0.4.6'}});this.write({method:'initialized',params:{}});this.ready=true}catch(error){this.stopProcess();throw error}
  })().finally(()=>{this.starting=null});return this.starting;
 }
 notification(message){const p=message.params||{};
  if(message.method==='account/login/completed'&&this.login&&p.loginId===this.login.loginId){this.clearLogin();this.error=p.success?'':'登录未完成，请重试';this.changed();if(p.success)this.refresh(true)}
  else if(message.method==='account/updated'){if(p.authMode===null){this.account=null;this.limits={};this.stats=null;this.updatedAt=0;this.statsUpdatedAt=0;this.changed()}}
  else if(message.method==='account/rateLimits/updated'&&this.account){const limits=sanitizeLimits(p);if(Object.keys(limits).length){this.limits={...this.limits,...limits};this.updatedAt=Date.now();this.changed()}}
 }
 async readAccount(){const result=await this.rpc('account/read',{refreshToken:false});const a=result.account;this.account=a?.type==='chatgpt'?{email:typeof a.email==='string'?a.email.slice(0,200):null,planType:typeof a.planType==='string'?a.planType.slice(0,40):null}:null;return this.account}
 async refresh(force=false){
  if(this.closed||this.login||(!force&&!this.account&&!this.hasLogin()))return this.snapshot();if(this.pending)return this.pending;
  if(!force&&Date.now()-this.attemptedAt<60000)return this.snapshot();this.attemptedAt=Date.now();const generation=this.generation;
  this.pending=(async()=>{try{await this.ensure();await this.readAccount();if(generation!==this.generation)return;if(!this.account){this.limits={};this.stats=null;this.updatedAt=0;this.statsUpdatedAt=0;this.error='';return}
   const results=await Promise.allSettled([this.rpc('account/rateLimits/read'),this.rpc('account/usage/read')]);if(generation!==this.generation)return;
   if(results[0].status==='fulfilled'){this.limits=sanitizeLimits(results[0].value);this.updatedAt=Date.now();this.error=''}else this.error=results[0].reason.message;
   if(results[1].status==='fulfilled'){this.stats=sanitizeUsage(results[1].value);this.statsUpdatedAt=Date.now();this.statsError=''}else this.statsError=results[1].reason.message;
  }catch(error){if(generation===this.generation)this.error=error.message}finally{this.pending=null;this.changed()}})();await this.pending;return this.snapshot();
 }
 async connect(type='chatgpt'){
  if(this.connecting)return this.connecting;if(this.login){await this.reopenLogin();return this.snapshot()}
  this.connecting=(async()=>{try{if(this.pending)await this.pending;await this.ensure();this.error='';const result=await this.rpc('account/login/start',{type:type==='chatgptDeviceCode'?'chatgptDeviceCode':'chatgpt'},45000);
   if(!result.loginId)throw Error('登录接口没有返回有效会话');const url=loginUrl(result.authUrl||result.verificationUrl);
   this.login={loginId:result.loginId,type:result.type,url,userCode:result.userCode,startedAt:Date.now()};this.loginTimer=setTimeout(()=>{this.cancelLogin().catch(()=>{});this.error='登录已超时，请重试';this.changed()},10*60000);this.loginTimer.unref?.();this.changed();await this.reopenLogin();
  }catch(error){this.error=error.message;this.changed();throw error}finally{this.connecting=null}return this.snapshot()})();return this.connecting;
 }
 async reopenLogin(){if(!this.login)return;try{await this.openExternal(loginUrl(this.login.url))}catch{this.error='无法打开浏览器，请点击重新打开登录页';this.changed()}}
 clearLogin(){clearTimeout(this.loginTimer);this.loginTimer=null;this.login=null}
 async cancelLogin(){const login=this.login;this.clearLogin();this.changed();if(login)await this.rpc('account/login/cancel',{loginId:login.loginId});return this.snapshot()}
 async disconnect(){if(this.connecting)await this.connecting.catch(()=>{});if(this.login)await this.cancelLogin();this.generation++;if(this.pending)await this.pending;await this.ensure();await this.rpc('account/logout');this.account=null;this.limits={};this.stats=null;this.updatedAt=0;this.statsUpdatedAt=0;this.error='';this.statsError='';this.stopProcess();this.changed();return this.snapshot()}
 start(){if(this.timer)return;this.refresh();this.timer=setInterval(()=>this.refresh(),60000);this.timer.unref?.()}
 stopProcess(){this.ready=false;const child=this.child;this.child=null;for(const w of this.waiters.values()){clearTimeout(w.timer);w.reject(Error('账号接口连接已关闭'))}this.waiters.clear();child?.stdin.end();child?.kill()}
 close(){this.closed=true;this.generation++;clearInterval(this.timer);this.timer=null;this.clearLogin();this.stopProcess()}
}
module.exports={AiAccount,usageFor,sanitizeLimits,sanitizeUsage,windowOf,loginUrl};
