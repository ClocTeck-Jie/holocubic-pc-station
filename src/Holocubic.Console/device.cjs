'use strict';
const http=require('node:http');
const os=require('node:os');
const net=require('node:net');
function host(input){
 const value=String(input||'').trim();
 const u=new URL(value.includes('://')?value:'http://'+value);
 if(u.protocol!=='http:'||u.username||u.password||u.pathname!=='/'||u.search||u.hash)throw Error('请输入局域网设备 IP 或 .local 地址，可带端口');
 const h=u.hostname;
 const a=h.split('.').map(Number);
 const local=net.isIPv4(h)&&(a[0]===10||a[0]===192&&a[1]===168||a[0]===172&&a[1]>=16&&a[1]<=31||a[0]===169&&a[1]===254);
 if(!local&&!/^[a-z0-9-]+\.local$/i.test(h))throw Error('仅支持局域网 IP 和 .local 设备地址');
 return u.host;
}
function request(address,route='/api/system/state',method='GET',body,timeout=5000){
 const base='http://'+host(address);
 const u=new URL(route,base);
 if(u.origin!==base||!['GET','POST','PUT','DELETE'].includes(method))throw Error('设备请求地址无效');
 const payload=body==null?null:typeof body==='string'?body:JSON.stringify(body);
 return new Promise((resolve,reject)=>{
  const req=http.request(u,{method,headers:{Accept:'application/json',...(payload?{'Content-Type':'application/json; charset=utf-8','Content-Length':Buffer.byteLength(payload)}:{})}},res=>{
   let count=0;const chunks=[];
   res.on('data',c=>{count+=c.length;if(count>8*1024*1024)req.destroy(Error('设备响应过大'));else chunks.push(c)});
   res.on('end',()=>{let data;const text=Buffer.concat(chunks).toString('utf8');try{data=JSON.parse(text)}catch{data=text}
    if(res.statusCode<200||res.statusCode>=300||data?.ok===false)reject(Error(data?.error||data?.message||'HTTP '+res.statusCode));else resolve(data);
   });res.on('error',reject);
  });
  const timer=setTimeout(()=>req.destroy(Error('连接超时，请确认设备和电脑在同一局域网')),timeout);
  req.on('close',()=>clearTimeout(timer));req.on('error',reject);if(payload)req.write(payload);req.end();
 });
}
async function probe(address,timeout=1500){const ip=host(address);const data=await request(ip,'/api/system/state','GET',null,timeout);if(!data||!Array.isArray(data.apps)||!data.main_path)throw Error('该地址没有返回 Cubic 设备接口');return {ip,name:'Cubic '+ip,online:true,system:data}}
function appIcon(address,id){
 if(!/^[a-z0-9_-]{1,100}$/i.test(id))return Promise.resolve(null);
 return new Promise(resolve=>{const req=http.get('http://'+host(address)+'/api/system/fs/file?path='+encodeURIComponent('/sd/apps/'+id+'/main.png'),res=>{const chunks=[];let length=0;if(res.statusCode!==200){res.resume();resolve(null);return}res.on('data',c=>{length+=c.length;if(length>1024*1024){req.destroy();resolve(null)}else chunks.push(c)});res.on('end',()=>{const bytes=Buffer.concat(chunks);resolve(bytes.subarray(0,8).equals(Buffer.from([137,80,78,71,13,10,26,10]))?'data:image/png;base64,'+bytes.toString('base64'):null)});res.on('error',()=>resolve(null))});const timer=setTimeout(()=>{req.destroy();resolve(null)},3000);req.on('close',()=>clearTimeout(timer));req.on('error',()=>resolve(null))});
}
async function localAddress(address){const {Socket}=require('node:dgram');const dns=require('node:dns').promises;const hostname=new URL('http://'+host(address)).hostname;const ip=net.isIPv4(hostname)?hostname:(await dns.lookup(hostname,{family:4})).address;return new Promise((resolve,reject)=>{const socket=require('node:dgram').createSocket('udp4');socket.on('error',e=>{socket.close();reject(e)});socket.connect(9,ip,()=>{const local=socket.address().address;socket.close();resolve(local)})})}
const num=s=>s.split('.').reduce((n,v)=>(n*256+Number(v))>>>0,0);
const ip=n=>[n>>>24,(n>>>16)&255,(n>>>8)&255,n&255].join('.');
function candidates(){
 const set=new Set(),ranges=[];
 for(const [name,addresses] of Object.entries(os.networkInterfaces())){
  if(/loopback|virtual|vmware|vethernet|docker|tailscale|zerotier|wsl/i.test(name))continue;
  for(const a of addresses||[]){if(a.family!=='IPv4'||a.internal)continue;try{host(a.address)}catch{continue}
   const addr=num(a.address);let mask=num(a.netmask),size=(~mask)>>>0;
   if(size>1023){mask=0xffffff00;size=255;}
   const base=(addr&mask)>>>0;ranges.push(ip(base)+'/'+(32-Math.log2(size+1)));
   for(let n=1;n<size;n++){const candidate=ip(base+n);if(candidate!==a.address)set.add(candidate)}
  }
 }
 return {addresses:[...set],ranges};
}
async function scan(progress,signal){
 const {addresses,ranges}=candidates();let cursor=0,done=0;const found=[];
 progress({done,total:addresses.length,ranges,found});
 await Promise.all(Array.from({length:Math.min(32,addresses.length)},async()=>{while(cursor<addresses.length&&!signal.aborted){const address=addresses[cursor++];try{const d=await probe(address,900);found.push(d);progress({done,total:addresses.length,ranges,found:[...found]})}catch{}done++;if(done%32===0||done===addresses.length)progress({done,total:addresses.length,ranges,found:[...found]})}}));
 return {found,ranges,total:addresses.length,done,cancelled:signal.aborted};
}
module.exports={host,request,probe,candidates,scan,appIcon,localAddress};
