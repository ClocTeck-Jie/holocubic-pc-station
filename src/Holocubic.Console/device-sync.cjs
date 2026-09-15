'use strict';
class DeviceSync {
 constructor({request,publish}){this.request=request;this.publish=publish;this.pending=new Map();this.snapshots=new Map();this.metadata=new Map();}
 invalidate(address){for(const key of this.metadata.keys())if(key.startsWith(address+'|'))this.metadata.delete(key);}
 refresh(address){
  if(this.pending.has(address))return this.pending.get(address);
  const job=this.read(address).finally(()=>this.pending.delete(address));this.pending.set(address,job);return job;
 }
 async read(address){
  try{
   const system=await this.request(address,'/api/system/state');
   if(!Array.isArray(system?.apps))throw Error('设备返回数据不正确');
   const previous=this.snapshots.get(address);let services=previous?.services||[],servicesError='';
   try{services=(await this.request(address,'/api/system/services')).services||[];}catch(e){servicesError=e.message;}
   const snapshot={address,system,services,servicesError,online:true,time:Date.now()};
   this.snapshots.set(address,snapshot);this.publish(snapshot);
   this.enrich(address,system).catch(()=>{});return snapshot;
  }catch(e){const snapshot={...this.snapshots.get(address),address,online:false,error:e.message,time:Date.now()};this.publish(snapshot);throw e;}
 }
 async enrich(address,system){
  const apps=system.installed_apps||system.apps||[];const metadata={};
  for(let i=0;i<apps.length;i+=3)await Promise.all(apps.slice(i,i+3).map(async a=>{
   if(!/^[a-z0-9_-]+$/i.test(a.id))return;
   const key=address+'|'+a.id+'|'+a.version;let entry=this.metadata.get(key);
   if(!entry||Date.now()-entry.time>60000){try{
    const text=await this.request(address,'/api/system/fs/file?path='+encodeURIComponent('/sd/apps/'+a.id+'/app.info'));
    const value={};for(const line of String(text).split(/\r?\n/)){const m=line.match(/^\s*(name(?:_[a-z_]+)?)\s*=\s*(.*?)\s*$/i);if(m)value[m[1]]=m[2];}
    entry={value,time:Date.now()};this.metadata.set(key,entry);
   }catch{entry={value:{}};}}
   metadata[a.id]=entry.value;
  }));
  if(this.snapshots.get(address)?.system===system)this.publish({...this.snapshots.get(address),metadata});
 }
}
module.exports={DeviceSync};
