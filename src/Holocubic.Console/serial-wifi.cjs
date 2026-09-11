'use strict';
const crypto=require('node:crypto');
class SerialWifi{
 constructor({nativeCall,open,close,status,emit,onConnected}){Object.assign(this,{nativeCall,open,close,status,emit,onConnected});this.buffer='';this.pending=new Map();this.active=false;this.cancelled=false;this.ready=null;this.last={status:'idle',message:'连接 USB 串口后可扫描和配置 Wi-Fi',networks:[]}}
 update(value){this.last={...this.last,...value};this.emit('serial-wifi',this.last)}
 feed(text){this.buffer=(this.buffer+text).slice(-120000);const lines=this.buffer.split('\n');this.buffer=lines.pop();for(const line of lines){const i=line.indexOf('@CUBIC_WIFI/1 ');if(i<0)continue;try{const data=JSON.parse(line.slice(i+14).trim());this.receive(data)}catch{}}}
 receive(data){
  if(data.status==='ready'&&['wifi_guide','serial_wifi_setup'].includes(data.app)){this.ready?.();this.update({status:'ready',message:'设备串口已就绪'});return;}
  const request=this.pending.get(data.id);if(!request)return;
  const messages={scan_result:'Wi-Fi 扫描完成',success:'Wi-Fi 连接成功',error:'设备配网失败',connecting:'设备正在连接 Wi-Fi',provisioning:'正在保存 Wi-Fi 配置'};
  this.update({...data,message:data.message||messages[data.status]||'等待设备处理…',networks:Array.isArray(data.networks)?data.networks:this.last.networks});
  if(['scan_result','success','error'].includes(data.status)){clearTimeout(request.timer);this.pending.delete(data.id);data.status==='error'?request.reject(Error(data.message||data.error||'设备配网失败')):request.resolve(data);if(data.status==='success'){const address=data.ip||data.sta_ip||data.address;if(address)this.onConnected(address).catch(()=>{});}}
 }
 async send(data){await this.nativeCall('write',{text:'@CUBIC_WIFI/1 '+JSON.stringify(data)+'\n'})}
 async handshake(port,baud){
  if(!this.status()||this.status().port!==port||this.status().baud!==Number(baud)){await this.open({port,baud});this.update({status:'waiting',message:'等待串口稳定…'});await new Promise(r=>setTimeout(r,2500));}
  let ready=false;this.ready=()=>{ready=true};const id=crypto.randomUUID();
  try{for(let n=0;n<30;n++){if(this.cancelled)throw Error('配网操作已取消');if(ready)return;this.update({status:'waiting',message:'正在握手；请在设备上打开 WiFi Setting Guide 应用'});try{await this.send({cmd:'hello',id})}catch{if(this.cancelled)throw Error('已取消');await (this.close?this.close():this.nativeCall('close')).catch(()=>{});await this.open({port,baud});await new Promise(r=>setTimeout(r,2500));}await new Promise(r=>setTimeout(r,800));if(ready)return}throw Error('设备未响应，请在设备上打开 WiFi Setting Guide 后重试')}finally{this.ready=null}
 }
 async run(command,{port,baud=115200,ssid,pwd}){
  if(this.active)throw Error('串口配网正在进行，请等待或取消');
  if(command==='provision'&&(typeof ssid!=='string'||ssid.trim().length<1||ssid.length>64||typeof pwd!=='string'||pwd.length>128))throw Error('Wi-Fi 名称或密码长度无效');
  this.active=true;this.cancelled=false;
  try{if(!port)throw Error('请选择设备串口');await this.handshake(port,Number(baud));if(this.cancelled)throw Error('已取消');const id=crypto.randomUUID();
   const result=new Promise((resolve,reject)=>{const timer=setTimeout(()=>{this.pending.delete(id);reject(Error(command==='scan'?'Wi-Fi 扫描超时':'设备连接超时，请检查 Wi-Fi 名称和密码'))},command==='scan'?30000:60000);this.pending.set(id,{resolve,reject,timer})});
   this.update({status:command==='scan'?'scanning':'connecting',message:command==='scan'?'正在扫描附近的 Wi-Fi…':'凭据已通过串口发送，等待设备连接…'});
   try{await this.send({cmd:command,id,...(command==='provision'?{ssid:ssid.trim(),pwd}: {})})}catch(e){const p=this.pending.get(id);clearTimeout(p.timer);p.reject(e);this.pending.delete(id)}
   return await result;
  }catch(e){this.update({status:'error',message:e.message});throw e}finally{this.active=false}
 }
 cancel(){this.cancelled=true;for(const p of this.pending.values()){clearTimeout(p.timer);p.reject(Error('已取消等待；设备已收到的连接请求可能仍在进行'))}this.pending.clear();this.update({status:'cancelled',message:'已取消等待'})}
}
module.exports={SerialWifi};
