'use strict';
const fs=require('node:fs'),path=require('node:path'),os=require('node:os');
const {publish}=require('./integrations/ai-status.cjs');
const {usageFor}=require('./ai-account.cjs');
const events=['SessionStart','UserPromptSubmit','PreToolUse','PostToolUse','PermissionRequest','PreCompact','PostCompact','SubagentStart','SubagentStop','Stop','Interrupt','SessionEnd'];
const psQuote=s=>"'"+String(s).replace(/'/g,"''")+"'";
class HolopetAI{
 constructor({appDir,dataDir,executable,start,log,configHome,localMonitor,account}){Object.assign(this,{appDir,dataDir,executable,start,log,localMonitor,account});this.configHome=configHome||process.env.CODEX_HOME||path.join(os.homedir(),'.codex');this.script=path.join(appDir,'integrations','ai-status.cjs');this.extension=path.join(appDir,'integrations','ai-browser-extension');}
 configFile(){return path.join(this.configHome,'hooks.json')}
 readConfig(){try{return JSON.parse(fs.readFileSync(this.configFile(),'utf8').replace(/^\uFEFF/,''))}catch(e){if(e.code==='ENOENT')return {};throw Error('客户端 Hook 配置无法读取，请先修复 hooks.json：'+e.message)}}
 own(hook){const command=String(hook?.commandWindows||hook?.command||'').replace(/\\/g,'/');return command.includes(this.script.replace(/\\/g,'/'))||command.includes('/holocubic-ui/integrations/codex-status.cjs')}
 async status(){let configured=false,configError='';try{const config=this.readConfig();configured=events.every(event=>(config.hooks?.[event]||[]).some(group=>(group.hooks||[]).some(h=>this.own(h))))}catch(e){configError=e.message}
  let last=null,running=false,clients=0;try{const [s,r]=await Promise.all([fetch('http://127.0.0.1:17321/status',{signal:AbortSignal.timeout(1000)}),fetch('http://127.0.0.1:17321/',{signal:AbortSignal.timeout(1000)})]);if(s.ok){last=await s.json();running=true}if(r.ok){const data=await r.json();clients=Number(data.clients||0)}}catch{}
  return {configured,configError,running,clients,last,local:this.localMonitor?.snapshot(),account:this.account?.snapshot(),extensionPath:this.extension,configFile:this.configFile()};
 }
 async syncAccount(){
  if(this.syncing)return;this.syncing=true;try{const r=await fetch('http://127.0.0.1:17321/status',{signal:AbortSignal.timeout(1000)});if(!r.ok)return;const last=await r.json();
   if(!['openai-local-log','openai-hooks','codex-hooks','chatgpt-web','openai-account'].includes(last.source)&&last.event!=='BridgeStart')return;
   const usage=usageFor(this.account?.limits,last.model||'');if(!usage&&!last.account_usage_at)return;
   const value={};for(const key of ['state','event','source','session','project','model','effort','tool','context','sent_at'])if(last[key]!==undefined)value[key]=last[key];
   if(last.event==='BridgeStart')Object.assign(value,{state:'idle',event:'AccountUsage',source:'openai-account',sent_at:Date.now()});
   Object.assign(value,{usage,usage_summary:this.account?.stats?.summary||null,account_usage_at:usage?this.account.updatedAt:null});
   if(JSON.stringify([last.usage,last.usage_summary,last.account_usage_at])!==JSON.stringify([value.usage,value.usage_summary,value.account_usage_at]))await publish(value);
  }catch{}finally{this.syncing=false}
 }
 async install(){
  const config=this.readConfig();if(config.hooks!=null&&(typeof config.hooks!=='object'||Array.isArray(config.hooks)))throw Error('hooks.json 的 hooks 格式无效');config.hooks||={};
  // The shipped Electron binary also supplies the Node runtime; no extra Node installation is required.
  const command="$env:ELECTRON_RUN_AS_NODE='1'; & "+psQuote(this.executable)+' '+psQuote(this.script);
  for(const event of events){const prior=config.hooks[event]||[];if(!Array.isArray(prior))throw Error('hooks.json 的事件配置格式无效：'+event);
   const groups=prior.map(group=>({...group,hooks:(group.hooks||[]).filter(h=>!this.own(h))})).filter(group=>group.hooks.length);
   groups.push({hooks:[{type:'command',command,commandWindows:command,timeout:3}]});config.hooks[event]=groups;
  }
  const file=this.configFile();fs.mkdirSync(path.dirname(file),{recursive:true});
  if(fs.existsSync(file)){const dir=path.join(this.dataDir,'integration-backups');fs.mkdirSync(dir,{recursive:true});fs.copyFileSync(file,path.join(dir,'hooks-'+Date.now()+'.json'))}
  const temp=file+'.holocubic-'+process.pid+'.tmp';fs.writeFileSync(temp,JSON.stringify(config,null,2)+'\n');fs.renameSync(temp,file);
  this.log('已配置 ChatGPT / Codex 本地任务同步');await this.start();return this.status();
 }
 async test(){await this.start();if(!await publish({state:'notification',event:'ConnectionTest',source:'holocubic-test',project:'Holopet',tool:'',model:'',sent_at:Date.now()}))throw Error('Holopet 服务未收到测试事件');return this.status()}
}
module.exports={HolopetAI,events};
