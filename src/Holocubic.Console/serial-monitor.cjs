'use strict';
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const clean=text=>String(text).replace(/\x1b\[[0-?]*[ -/]*[@-~]/g,'');
const day=date=>[date.getFullYear(),String(date.getMonth()+1).padStart(2,'0'),String(date.getDate()).padStart(2,'0')].join('-');
const stamp=date=>day(date)+' '+date.toTimeString().slice(0,8)+'.'+String(date.getMilliseconds()).padStart(3,'0');
const safe=value=>String(value||'unknown').replace(/[^a-zA-Z0-9_.-]/g,'_').replace(/^\.+/,'_').slice(0,100);

// Boot output often describes the same restart several times. Keep one event per boot.
class CrashDetector{
 constructor(onEvent,now=()=>Date.now()){this.onEvent=onEvent;this.now=now;this.boot=null;this.crash=null;}
 accept(raw){
  const line=clean(raw).trim(),time=this.now();
  const crash=/Guru Meditation Error|\bpanic'ed\b|^assert failed:|^abort\(\) was called|CORRUPT HEAP:|Stack canary watchpoint triggered|stack overflow|Task watchdog got triggered|Interrupt wdt timeout|task_wdt:.*(?:triggered|did not reset)/i.test(line);
  if(crash){if(!this.crash){this.boot=null;this.crash={id:crypto.randomUUID(),type:'crash',label:'设备死机',reason:line,time};this.onEvent({...this.crash});}return;}
  const rom=/^(?:ESP-ROM:|ets (?:Jun|Jul|Aug|Sep|Oct|Nov|Dec|Jan|Feb|Mar|Apr|May))/.test(line),rst=/^rst:0x[0-9a-f]+\b/i.test(line),reboot=/^Rebooting\.{0,3}$/i.test(line),reason=/\[BOOT\]\s*(?:reset_reason=|last reset was )/i.test(line);
  if(!(rom||rst||reboot||reason))return;
  const phase=reboot?'reboot':rom?'rom':rst?'rst':'reason';
  let fresh=!this.boot||time-this.boot.time>120000;
  if(this.boot&&(reboot||rom)&&this.boot[phase]&&(this.boot.rst||this.boot.reasonSeen||time-this.boot.time>1000))fresh=true;
  if(fresh){const abnormal=!!this.crash||/panic|watchdog|wdt|brownout/i.test(line);this.boot={id:crypto.randomUUID(),type:'restart',label:abnormal?'设备异常重启':'设备重启',reason:line,time,abnormal,crashId:this.crash?.id};this.crash=null;if(reason)this.boot.reasonSeen=true;else this.boot[phase]=true;this.onEvent({...this.boot});}
  else{if(reason)this.boot.reasonSeen=true;else this.boot[phase]=true;if(reason||rst){const abnormal=this.boot.abnormal||/panic|watchdog|wdt|brownout/i.test(line);if(this.boot.reason!==line){Object.assign(this.boot,{reason:line,abnormal,label:abnormal?'设备异常重启':'设备重启'});this.onEvent({...this.boot,update:true});}}}
 }
}

class SerialRecorder{
 constructor({root,device,port,baud,onEvent,now=()=>new Date()}){Object.assign(this,{root,device:{...device},port,baud,onEvent,now});this.dir=path.join(root,safe(device.ip));fs.mkdirSync(this.dir,{recursive:true});this.partial='';this.buffer='';this.history=[];this.capture=null;this.detector=new CrashDetector(e=>this.incident(e),()=>this.now().getTime());this.rotate();this.write(`[串口开始记录] ${device.name} | ${device.ip} | ${port} | ${baud} 8N1`);this.flush();}
 rotate(){const date=day(this.now());if(date===this.date&&this.fileSize<20*1024*1024)return;this.flush();this.date=date;let index=1,file;do{file=path.join(this.dir,`${date}-${safe(this.port)}-${String(index++).padStart(3,'0')}.log`);}while(fs.existsSync(file)&&fs.statSync(file).size>=20*1024*1024);this.file=file;this.fileSize=fs.existsSync(file)?fs.statSync(file).size:0;}
 write(text){this.buffer+=`[${stamp(this.now())}] ${text}\n`;if(this.buffer.length>65536)this.flush();}
 feed(text){this.partial+=text;let end;while((end=this.partial.indexOf('\n'))>=0){const line=this.partial.slice(0,end).replace(/\r$/,'');this.partial=this.partial.slice(end+1);this.line(line);}if(this.partial.length>32768){this.line(this.partial.slice(0,32768));this.partial=this.partial.slice(32768);}}
 line(line){this.rotate();this.write(line);const record=`[${stamp(this.now())}] ${line}\n`;if(this.capture){if(this.now().getTime()-this.capture.started>120000||this.capture.size>256*1024)this.capture=null;else{fs.appendFileSync(this.capture.file,record);this.capture.size+=Buffer.byteLength(record);}}this.history.push(record);while(this.history.length>180||this.history.reduce((n,s)=>n+s.length,0)>128000)this.history.shift();this.detector.accept(line);}
 incident(event){
  this.flush();const date=this.now(),reportDir=path.join(this.dir,'incidents');fs.mkdirSync(reportDir,{recursive:true});
  if(!event.update){const file=path.join(reportDir,`${day(date)}-${date.toTimeString().slice(0,8).replace(/:/g,'-')}-${event.type}-${event.id.slice(0,8)}.log`);const intro=`[${event.label}] ${event.reason}\n设备：${this.device.name} (${this.device.ip})\n串口：${this.port} / ${this.baud}\n时间：${date.toISOString()}\n完整记录：${this.file}\n\n--- 异常前后文 ---\n`;
   // A reboot following a panic belongs to the existing crash report.
   if(event.type==='restart'&&event.crashId&&this.capture){event.report=this.capture.file;fs.appendFileSync(this.capture.file,`\n[${stamp(date)}] [${event.label}] ${event.reason}\n`);}
   else{fs.writeFileSync(file,intro+this.history.join(''));this.capture={file,started:date.getTime(),size:Buffer.byteLength(intro+this.history.join(''))};event.report=file;}
  }else{event.report=this.capture?.file;if(this.capture)fs.appendFileSync(this.capture.file,`[${stamp(date)}] [启动原因] ${event.reason}\n`);}
  const entry={...event,time:date.toISOString(),device:this.device,port:this.port,baud:this.baud,serialLog:this.file};fs.appendFileSync(path.join(this.dir,`events-${day(date)}.jsonl`),JSON.stringify(entry)+'\n');this.onEvent(entry);
 }
 flush(){if(!this.buffer||!this.file)return;const text=this.buffer;fs.appendFileSync(this.file,text);this.fileSize+=Buffer.byteLength(text);this.buffer='';}
 close(reason='串口已断开'){if(this.partial){this.line(this.partial);this.partial='';}this.write(`[串口结束记录] ${reason}`);this.flush();}
}

class SerialMonitor{
 constructor({nativeCall,prefs,savePrefs,emit,log,dataDir,isProvisioning=()=>false}){Object.assign(this,{nativeCall,prefs,savePrefs,emit,log,isProvisioning});this.root=path.join(dataDir,'serial-logs');this.connection=null;this.recorder=null;this.status='waiting';this.message='等待 USB 串口';this.paused=new Set();this.queue=Promise.resolve();this.closed=false;this.lastAttempt=0;this.lastNotice='';this.text='';}
 snapshot(){const p=this.prefs(),device=p.devices.find(d=>d.ip===p.active);return {enabled:p.autoSerialLog!==false,connection:this.connection,status:this.status,message:this.message,path:this.root,file:this.recorder?.file,device:device?.ip,binding:device?.serialPort||'',baud:device?.serialBaud||115200,lastEvent:this.lastEvent?.device.ip===p.active?this.lastEvent:undefined,text:this.textAddress===p.active?this.text:''};}
 update(status,message){const changed=this.status!==status||this.message!==message;this.status=status;this.message=message;if(changed)this.emit('serial-monitor',this.snapshot());}
 exclusive(fn){const result=this.queue.then(fn);this.queue=result.catch(()=>{});return result;}
 start(){this.timer=setInterval(()=>this.tick().catch(e=>this.update('error',e.message)),4000);this.flushTimer=setInterval(()=>{try{this.recorder?.flush();}catch(e){this.update('error','串口日志写入失败：'+e.message);}},500);this.tick().catch(e=>this.update('error',e.message));}
 tick(){if(this.closed||this.ticking)return Promise.resolve();this.ticking=true;return this.exclusive(async()=>{const p=this.prefs(),address=p.active;if(this.closed||this.isProvisioning())return;if(this.connection&&this.connection.address!==address)await this.disconnect(false,'切换设备');if(this.connection)return;if(p.autoSerialLog===false){this.update('disabled','自动串口记录已关闭');return;}if(this.paused.has(address)){this.update('paused','串口记录已暂停，点击连接可恢复');return;}const d=p.devices.find(d=>d.ip===address);if(!d)return;const ports=await this.nativeCall('ports');if(this.closed||this.prefs().active!==address)return;let port=d.serialPort;if(port&&!ports.includes(port)){this.update('waiting','等待已绑定串口 '+port);return;}if(!port){const available=ports.filter(port=>!p.devices.some(other=>other.ip!==address&&other.serialPort===port));if(available.length===1&&ports.length===1)port=available[0];else{this.update('waiting',ports.length?'多个设备或串口，请选择串口并连接以绑定':'等待 USB 串口');return;}}if(Date.now()-this.lastAttempt<8000)return;this.lastAttempt=Date.now();try{await this.connect({port,baud:d.serialBaud||115200},address,true);}catch(e){this.update('error','无法自动连接串口 '+port+'：'+e.message);if(this.lastNotice!==this.message){this.log(this.message);this.lastNotice=this.message;}}}).finally(()=>this.ticking=false);}
 async connect({port,baud=115200},address,automatic=false){
  port=String(port||'').toUpperCase();baud=Number(baud);if(!/^COM[1-9][0-9]{0,3}$/.test(port)||!Number.isInteger(baud)||baud<1200||baud>3000000)throw Error('串口参数不正确');
  if(this.connection?.port===port&&this.connection.baud===baud&&this.connection.address===address)return this.connection;
  await this.disconnect(false,'重新连接串口');
  // Always release the native handle after USB loss before reopening the same COM port.
  await this.nativeCall('close');const d=this.prefs().devices.find(d=>d.ip===address);if(!d)throw Error('设备不存在');if(this.closed)return null;
  const context={port,baud,address,name:d.name,sessionId:crypto.randomUUID()};this.text='';this.textAddress=address;
  try{this.recorder=new SerialRecorder({root:this.root,device:d,port,baud,onEvent:e=>this.incident(e)});this.connection=context;await this.nativeCall('open',{port,baud});if(!this.connection)throw Error('串口连接已中断');}catch(e){this.recorder?.close('串口打开失败');this.recorder=null;this.connection=null;throw e;}
  if(this.closed){await this.disconnect(false);return null;}
  // An explicit port selection rebinds that COM port to the chosen device.
  if(!automatic)for(const other of this.prefs().devices)if(other.ip!==address&&other.serialPort===port){delete other.serialPort;delete other.serialBaud;}
  if(d.serialPort!==port||d.serialBaud!==baud){d.serialPort=port;d.serialBaud=baud;this.savePrefs();}
  this.paused.delete(address);this.lastNotice='';this.update('recording','正在记录串口日志');this.emit('serial-state',context);this.log(`串口日志开始 ${d.name} ${address} ${port} ${baud}`);return context;
 }
 open(data){const address=this.prefs().active;return this.exclusive(()=>this.connect(data,address));}
 bind(address){return this.exclusive(async()=>{const c=this.connection,d=this.prefs().devices.find(d=>d.ip===address);if(!c||!d||c.address===address)return;this.recorder?.close('串口配网确认设备地址 '+address);for(const other of this.prefs().devices)if(other.ip!==address&&other.serialPort===c.port){delete other.serialPort;delete other.serialBaud;}d.serialPort=c.port;d.serialBaud=c.baud;this.savePrefs();this.text='';this.textAddress=address;this.recorder=new SerialRecorder({root:this.root,device:d,port:c.port,baud:c.baud,onEvent:e=>this.incident(e)});this.connection={...c,address,name:d.name,sessionId:crypto.randomUUID()};this.emit('serial-state',this.connection);this.emit('serial-monitor',this.snapshot());});}
 async disconnect(pause=true,reason='串口已断开'){const prior=this.connection;if(pause)this.paused.add(this.prefs().active);if(prior)await this.nativeCall('close').catch(()=>{});this.recorder?.close(reason);this.recorder=null;this.connection=null;this.emit('serial-state',null);this.update(pause?'paused':'waiting',pause?'串口记录已暂停，点击连接可恢复':'等待 USB 串口');}
 closePort(){return this.exclusive(()=>this.disconnect(true));}
 feed(text,port){if(!this.connection||port&&port!==this.connection.port)return;this.text=(this.text+text).slice(-100000);try{this.recorder?.feed(text);}catch(e){this.update('error','串口日志写入失败：'+e.message);}}
 lost(message,port){if(this.closed||port&&port!==this.connection?.port)return;try{this.recorder?.close('USB 串口中断：'+message);}catch{}this.recorder=null;this.connection=null;this.emit('serial-state',null);this.update('waiting','USB 串口中断，等待自动重连');this.log('串口中断：'+message);}
 incident(event){this.lastEvent=event;if(!event.update){this.log(`[${event.label}] ${event.device.name} ${event.device.ip} ${event.port} ${event.reason} | 报告：${event.report}`);this.emit('device-incident',event);}this.emit('serial-monitor',this.snapshot());}
 setEnabled(enabled){this.prefs().autoSerialLog=!!enabled;this.savePrefs();this.paused.delete(this.prefs().active);if(!enabled)return this.exclusive(()=>this.disconnect(false)).then(()=>{this.update('disabled','自动串口记录已关闭');return this.snapshot();});return this.tick().then(()=>this.snapshot());}
 selected(){this.paused.delete(this.prefs().active);this.lastAttempt=0;this.tick().catch(e=>this.update('error',e.message));}
 stop(){this.closed=true;clearInterval(this.timer);clearInterval(this.flushTimer);try{this.recorder?.close('控制台退出');}catch{}this.recorder=null;}
}
module.exports={CrashDetector,SerialRecorder,SerialMonitor};
